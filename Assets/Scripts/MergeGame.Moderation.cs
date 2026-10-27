// Действия администратора, пришедшие с сервера:
// • изменения ресурсов (монеты, кристаллы, энергия) — сервер присылает их в ответе на статус,
//   игра применяет, запоминает номера применённых (чтобы не применить дважды) и подтверждает серверу;
// • блокировка — игра показывает экран «Игрок заблокирован» поверх всего и не даёт играть,
//   пока администратор не снимет блокировку.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[Serializable] class NetGrant { public long id; public int coins, gems, energy; }
[Serializable] class NetStatusResp { public bool ok; public string error, reason; public NetGrant[] grants; }
[Serializable] class NetAckReq { public string id, token; public long[] ids; }

public partial class MergeGame
{
    const int MaxGrantedEnergy = 99;

    // подарки, применённые на устройстве, но ещё не попавшие в облачное сохранение аккаунта:
    // если сервер пришлёт более новое сохранение без них, они добавятся снова
    int pendCoins, pendGems, pendEnergy;

    bool banned;
    string banReason = "";
    RectTransform banOverlay;
    Text banReasonTxt, banIdTxt;

    void InitModeration()
    {
        banned = PlayerPrefs.GetInt("net_banned", 0) == 1;
        banReason = PlayerPrefs.GetString("net_ban_reason", "");
        var p = PlayerPrefs.GetString("grant_pending", "").Split(',');
        if (p.Length == 3) { int.TryParse(p[0], out pendCoins); int.TryParse(p[1], out pendGems); int.TryParse(p[2], out pendEnergy); }
        ApplyBanUi();
    }

    void SavePending()
    {
        PlayerPrefs.SetString("grant_pending", pendCoins + "," + pendGems + "," + pendEnergy);
    }

    // сохранение аккаунта успешно ушло на сервер — подарки в нём уже учтены
    void ClearPendingGrants()
    {
        if (pendCoins == 0 && pendGems == 0 && pendEnergy == 0) return;
        pendCoins = pendGems = pendEnergy = 0;
        SavePending();
    }

    // поверх нашего прогресса загрузили сохранение с сервера — вернуть неучтённые подарки
    void ReapplyPendingGrants()
    {
        if (pendCoins == 0 && pendGems == 0 && pendEnergy == 0) return;
        score = Mathf.Max(0, score + pendCoins);
        gems = Mathf.Max(0, gems + pendGems);
        energy = Mathf.Clamp(energy + pendEnergy, 0, MaxGrantedEnergy);
        Debug.Log($"[HatchMerge] Подарки возвращены после загрузки сохранения: {pendCoins:+0;-0} монет, {pendGems:+0;-0} крист., {pendEnergy:+0;-0} энергии");
        SaveLocal();
        MarkSaveDirty();
        RefreshTop();
    }

    // ---------- изменения ресурсов ----------
    IEnumerator ApplyGrants(NetGrant[] grants)
    {
        var done = LoadAppliedGrants();
        var ids = new List<long>();
        int coins = 0, gemsAdd = 0, energyAdd = 0;
        foreach (var g in grants)
        {
            if (g == null) continue;
            ids.Add(g.id);
            if (done.Contains(g.id)) continue;      // уже применено, но сервер не получил подтверждение
            done.Add(g.id);
            coins += g.coins; gemsAdd += g.gems; energyAdd += g.energy;
        }

        Debug.Log($"[HatchMerge] От сервера пришло изменений ресурсов: {grants.Length}, новых: {coins:+0;-0} монет, {gemsAdd:+0;-0} крист., {energyAdd:+0;-0} энергии");
        if (coins != 0 || gemsAdd != 0 || energyAdd != 0)
        {
            if (HasAccount) { pendCoins += coins; pendGems += gemsAdd; pendEnergy += energyAdd; SavePending(); }
            score = Mathf.Max(0, score + coins);
            gems = Mathf.Max(0, gems + gemsAdd);
            energy = Mathf.Clamp(energy + energyAdd, 0, MaxGrantedEnergy);
            SaveAppliedGrants(done);
            Save();
            PlayerPrefs.Save();
            RefreshTop();
            if (tab == TabStart) RefreshStartMenu();
            Toast(GrantText(coins, gemsAdd, energyAdd));
            GameAudio.Buy();
        }
        else SaveAppliedGrants(done);

        if (ids.Count == 0) yield break;
        var ack = new NetAckReq { id = netId, token = netToken, ids = ids.ToArray() };
        yield return Http("POST", netUrl, "/api/grants/ack", JsonUtility.ToJson(ack), (c, t) => { });
    }

    static string GrantText(int coins, int gemsAdd, int energyAdd)
    {
        var parts = new List<string>();
        void Part(int v, string unit) { if (v != 0) parts.Add((v > 0 ? "+" : "−") + Mathf.Abs(v) + " " + unit); }
        Part(coins, "монет");
        Part(gemsAdd, "крист.");
        Part(energyAdd, "энергии");
        bool gift = coins >= 0 && gemsAdd >= 0 && energyAdd >= 0;
        return (gift ? "Подарок: " : "Изменение: ") + string.Join(", ", parts);
    }

    static HashSet<long> LoadAppliedGrants()
    {
        var set = new HashSet<long>();
        foreach (var s in PlayerPrefs.GetString("grants_done", "").Split(','))
            if (long.TryParse(s, out long id)) set.Add(id);
        return set;
    }

    static void SaveAppliedGrants(HashSet<long> set)
    {
        var list = new List<long>(set);
        list.Sort();
        if (list.Count > 200) list.RemoveRange(0, list.Count - 200);   // хватит последних 200 номеров
        PlayerPrefs.SetString("grants_done", string.Join(",", list));
    }

    // ---------- блокировка ----------
    void SetBanned(bool on, string reason)
    {
        reason = reason ?? "";
        if (banned == on && banReason == reason) return;
        banned = on;
        banReason = on ? reason : "";
        PlayerPrefs.SetInt("net_banned", on ? 1 : 0);
        PlayerPrefs.SetString("net_ban_reason", banReason);
        PlayerPrefs.Save();
        if (on) { CancelDrag(); ClearHint(); Blur(); }
        else SetTab(TabStart);
        ApplyBanUi();
    }

    void BuildBanScreen()
    {
        banOverlay = NewRt("Banned", canvasRt);
        banOverlay.anchorMin = Vector2.zero;
        banOverlay.anchorMax = Vector2.one;
        banOverlay.offsetMin = banOverlay.offsetMax = Vector2.zero;
        AddImg(banOverlay, null, C(255, 243, 224));

        var box = CC(banOverlay, 0, 20, 350, 330, "Box");
        Round(box, Color.white, 28);
        var mark = TL(box, 145, 26, 60, 60, "Mark");
        AddImg(mark, circleSpr, Red);
        Label(mark, "!", 34, Color.white, TextAnchor.MiddleCenter, 0, 0, 60, 60);
        Label(box, "Игрок заблокирован", 24, Ink, TextAnchor.MiddleCenter, 0, 100, 350, 36);
        banReasonTxt = WrapLabel(box, "", 15, Ink, TextAnchor.UpperCenter, 24, 146, 302, 70);
        banIdTxt = Label(box, "", 14, Muted, TextAnchor.MiddleCenter, 0, 222, 350, 24);
        WrapLabel(box, "Если это ошибка — напишите администратору игры и назовите свой ID.",
            13, Muted, TextAnchor.UpperCenter, 24, 256, 302, 50);
        ApplyBanUi();
    }

    void ApplyBanUi()
    {
        if (banOverlay == null) return;
        banOverlay.gameObject.SetActive(banned);
        if (!banned) return;
        banReasonTxt.text = string.IsNullOrEmpty(banReason) ? "Причина не указана" : "Причина: " + banReason;
        banIdTxt.text = string.IsNullOrEmpty(netId) ? "" : "Ваш ID: " + netId;
    }
}
