// Жизнь зоопарка: уход за животными, альбом с наградами и редкие окраски.
//
// • Уход: в 3D-просмотре животного — «Покормить» (раз в 4 часа, приносит монеты) и «Погладить»
//   (просто радуется). В «Зоопарке» монетка на карточке — животное можно покормить;
//   кнопка «Покормить всех» кормит всех голодных сразу.
// • Альбом: животные разбиты на группы («Лесные», «Морские»…). Собрали группу — забирайте награду:
//   рамку, фон или стиль интерфейса. Награды записываются на сервер (как покупки), поэтому
//   с аккаунтом они есть на всех устройствах. Надеть/снять — там же, в альбоме.
// • Редкие окраски: животное иногда вылупляется золотым или ночным (5%), а ещё может
//   поменять окраску после кормления (2%). Редкие видны в зоопарке, в 3D и при вылуплении.
// Группы альбома дублируются на сервере (ALBUM в hatchmerge_server.py) — id наград должны совпадать.

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[Serializable] class NetAlbumReq { public string id, token, item; public int zoo; }

public partial class MergeGame
{
    const int TabAlbum = 11;
    const int CareSeconds = 4 * 3600;

    // ---------- редкие окраски ----------
    static readonly string[] VariantName = { "", "Золотая", "Ночная" };
    static readonly Color[] VariantTint = { Color.white, new Color32(255, 214, 110, 255), new Color32(120, 128, 205, 255) };
    static readonly int[] zooVar = new int[42];        // = Zoo.Length; 0 обычная, 1 золотая, 2 ночная
    readonly long[] careAt = new long[42];            // когда животное кормили (unix-время)

    // ---------- альбом ----------
    struct AlbumGroup { public string title, reward; public int[] animals; }
    static AlbumGroup Grp(string t, string r, params int[] a) => new AlbumGroup { title = t, reward = r, animals = a };

    static readonly AlbumGroup[] Album =
    {
        Grp("Сказочные", "fr_magic", 0, 1, 7, 11),
        Grp("Домашние", "fr_hearts", 4, 6, 12, 25, 26, 27, 36),
        Grp("Лесные", "ui_forest", 3, 9, 15, 23, 24, 28, 37, 41),
        Grp("Морские", "bg_sea", 2, 8, 18, 30, 31, 32),
        Grp("Летающие", "bg_sky", 5, 21, 35, 39, 40),
        Grp("У пруда", "fr_leaf", 13, 19, 20, 22),
        Grp("Из далёких стран", "ui_sunset", 10, 14, 16, 17, 29, 33, 34, 38),
    };

    RectTransform albumPanel;
    readonly Text[] albProg = new Text[7], albBtnTxt = new Text[7];
    readonly Image[] albBtnImg = new Image[7];
    readonly RectTransform[] albIcons = new RectTransform[7];
    Text feedAllTxt, galCareInfo;
    Image feedAllImg, galFeedImg;
    float galHappy;
    bool albumBusy;

    // ============================================================
    //  Запуск и сохранение
    // ============================================================
    void InitZooLife()
    {
        string v = PlayerPrefs.GetString("zoo_var", "");
        for (int i = 0; i < zooVar.Length; i++) zooVar[i] = i < v.Length ? Mathf.Clamp(v[i] - '0', 0, 2) : 0;
        var c = PlayerPrefs.GetString("zoo_care", "").Split(',');
        for (int i = 0; i < careAt.Length; i++) careAt[i] = i < c.Length && long.TryParse(c[i], out long t) ? t : 0;
    }

    void SaveZooLifeLocal()
    {
        var sb = new System.Text.StringBuilder(zooVar.Length);
        foreach (var x in zooVar) sb.Append((char)('0' + x));
        PlayerPrefs.SetString("zoo_var", sb.ToString());
        PlayerPrefs.SetString("zoo_care", string.Join(",", careAt));
    }

    void ResetZooLife()
    {
        Array.Clear(zooVar, 0, zooVar.Length);
        Array.Clear(careAt, 0, careAt.Length);
    }

    // ============================================================
    //  Редкие окраски
    // ============================================================
    int RollRareVariant(int idx)
    {
        zooVar[idx] = UnityEngine.Random.value < 0.05f ? UnityEngine.Random.Range(1, 3) : 0;
        return zooVar[idx];
    }

    static int MyVariant(int idx) => idx >= 0 && idx < zooVar.Length ? zooVar[idx] : 0;

    // ============================================================
    //  Уход
    // ============================================================
    bool CanFeed(int idx) => idx < zooCount && NowUnix() - careAt[idx] >= CareSeconds;

    int FeedReward(int idx) => 15 + Zoo[idx].target * 5;

    string FeedWait(int idx)
    {
        long left = CareSeconds - (NowUnix() - careAt[idx]);
        if (left <= 0) return "";
        long h = left / 3600, m = (left % 3600) / 60;
        return h > 0 ? h + " ч " + m + " мин" : Math.Max(1, m) + " мин";
    }

    // покормить одно животное; true — покормили
    bool FeedOne(int idx, bool quiet)
    {
        if (!CanFeed(idx)) return false;
        careAt[idx] = NowUnix();
        score += FeedReward(idx);
        if (zooVar[idx] == 0 && UnityEngine.Random.value < 0.02f)
        {
            zooVar[idx] = UnityEngine.Random.Range(1, 3);
            if (!quiet) Toast(Zoo[idx].name + ": новая окраска — " + VariantName[zooVar[idx]].ToLower() + "!");
        }
        return true;
    }

    void FeedFromGallery()
    {
        if (galIndex >= zooCount) { GameAudio.Deny(); Toast("Это животное ещё не вылупилось"); return; }
        if (!CanFeed(galIndex)) { GameAudio.Deny(); Toast("Ещё сыт. Покормить можно через " + FeedWait(galIndex)); return; }
        int v0 = zooVar[galIndex];
        FeedOne(galIndex, false);
        galHappy = 1.2f;
        GameAudio.Buy();
        if (zooVar[galIndex] == v0) Toast("Ням! +" + FeedReward(galIndex) + " монет");
        else ShowGalleryCreature();              // окраска поменялась — перестроить модель
        Burst(new Vector2(195, 300), C(255, 205, 90), 14, menuFx);
        QuestAdd(QuestKind.Feed);
        AfterCare();
    }

    void PetFromGallery()
    {
        if (galIndex >= zooCount) { GameAudio.Deny(); Toast("Это животное ещё не вылупилось"); return; }
        galHappy = 1.2f;
        GameAudio.Hatch();
        Burst(new Vector2(195, 290), C(245, 143, 168), 12, menuFx);
        string[] says = { "Мурр!", "Как приятно!", "Ещё-ещё!", "Хи-хи!", "Обожаю!" };
        Toast(Zoo[galIndex].name + ": " + says[UnityEngine.Random.Range(0, says.Length)]);
    }

    void FeedAll()
    {
        int n = 0, coins = 0;
        for (int i = 0; i < zooCount; i++)
        {
            int r = FeedReward(i);
            if (FeedOne(i, true)) { n++; coins += r; }
        }
        if (n == 0) { GameAudio.Deny(); Toast(zooCount == 0 ? "В зоопарке пока никого нет" : "Все сыты — загляните попозже"); return; }
        GameAudio.Buy();
        Toast("Покормили: " + n + " · +" + coins + " монет");
        Burst(new Vector2(280, 640), C(255, 205, 90), 16, menuFx);
        QuestAdd(QuestKind.Feed, n);
        AfterCare();
    }

    int HungryCount()
    {
        int n = 0;
        for (int i = 0; i < zooCount; i++) if (CanFeed(i)) n++;
        return n;
    }

    void AfterCare()
    {
        Save();
        RefreshTop();
        if (tab == 1) RefreshZooPanel();
        RefreshGalleryCare();
    }

    // кнопки ухода под 3D-моделью
    void BuildCareUi()
    {
        galCareInfo = Label(galleryPanel, "", 12, Muted, TextAnchor.MiddleCenter, 20, 596, 350, 18);
        var feed = TL(galleryPanel, 20, 618, 170, 44, "Feed");
        galFeedImg = Round(feed, Red, 22);
        Label(feed, "Покормить", 16, Color.white, TextAnchor.MiddleCenter, 0, 0, 170, 44);
        AddBtn(feed, FeedFromGallery, TabGallery);
        var pet = TL(galleryPanel, 200, 618, 170, 44, "Pet");
        Round(pet, C(255, 225, 235), 22);
        Label(pet, "Погладить", 16, Ink, TextAnchor.MiddleCenter, 0, 0, 170, 44);
        AddBtn(pet, PetFromGallery, TabGallery);
    }

    void RefreshGalleryCare()
    {
        if (galCareInfo == null) return;
        bool open = galIndex < zooCount;
        bool can = open && CanFeed(galIndex);
        galFeedImg.color = can ? Red : C(216, 201, 190);
        galCareInfo.text = !open ? "" : can ? "Голоден! Покормите — получите " + FeedReward(galIndex) + " монет"
                                             : "Сыт. Покормить можно через " + FeedWait(galIndex);
    }

    // ============================================================
    //  Зоопарк: кнопки «Альбом» и «Покормить всех»
    // ============================================================
    void BuildZooButtons()
    {
        var alb = TL(zooPanel, 20, 622, 166, 42, "Album");
        Round(alb, Color.white, 21);
        Label(alb, "Альбом", 16, Ink, TextAnchor.MiddleCenter, 0, 0, 166, 42);
        AddBtn(alb, () => SetTab(TabAlbum), 1);
        var fa = TL(zooPanel, 196, 622, 174, 42, "FeedAll");
        feedAllImg = Round(fa, Red, 21);
        feedAllTxt = Label(fa, "", 15, Color.white, TextAnchor.MiddleCenter, 0, 0, 174, 42);
        AddBtn(fa, FeedAll, 1);
    }

    void RefreshZooButtons()
    {
        if (feedAllTxt == null) return;
        int n = HungryCount();
        feedAllTxt.text = n > 0 ? "Покормить всех (" + n + ")" : "Все сыты";
        feedAllImg.color = n > 0 ? Red : C(216, 201, 190);
    }

    // ============================================================
    //  Альбом
    // ============================================================
    static bool GroupComplete(AlbumGroup g, int zoo)
    {
        foreach (var a in g.animals) if (a >= zoo) return false;
        return true;
    }

    static int GroupHave(AlbumGroup g, int zoo)
    {
        int n = 0;
        foreach (var a in g.animals) if (a < zoo) n++;
        return n;
    }

    void BuildAlbum()
    {
        albumPanel = TL(content, 0, 0, 390, 700, "AlbumPanel");
        var back = TL(albumPanel, 20, 82, 140, 36, "Back");
        Label(back, "← Зоопарк", 16, Red, TextAnchor.MiddleLeft, 0, 0, 140, 36);
        AddBtn(back, () => SetTab(1), TabAlbum);
        Label(albumPanel, "Альбом", 22, Ink, TextAnchor.MiddleLeft, 20, 116, 260, 32);
        Label(albumPanel, "Соберите всех животных группы — получите награду", 12, Muted, TextAnchor.MiddleLeft, 20, 146, 350, 18);

        for (int i = 0; i < Album.Length; i++)
        {
            int gi = i;
            var g = Album[i];
            var row = TL(albumPanel, 20, 172 + i * 70, 350, 64, "Group");
            Round(row, Color.white, 20);
            Label(row, g.title, 15, Ink, TextAnchor.MiddleLeft, 14, 6, 150, 22);
            albProg[i] = Label(row, "", 12, Muted, TextAnchor.MiddleLeft, 150, 6, 60, 22);
            albIcons[i] = TL(row, 10, 32, 200, 26, "Icons");
            int k = CosIndex(g.reward);
            var prev = TL(row, 212, 10, 44, 44, "Reward");
            if (k >= 0) DrawCosPreview(prev, CosItems[k]);
            var b = TL(row, 262, 15, 78, 34, "Claim");
            albBtnImg[i] = Round(b, Red, 17);
            albBtnTxt[i] = Label(b, "", 13, Color.white, TextAnchor.MiddleCenter, 0, 0, 78, 34);
            AddBtn(b, () => AlbumButton(gi), TabAlbum);
        }
    }

    void RefreshAlbum()
    {
        if (albumPanel == null) return;
        for (int i = 0; i < Album.Length; i++)
        {
            var g = Album[i];
            int have = GroupHave(g, zooCount);
            albProg[i].text = have + " из " + g.animals.Length;
            foreach (Transform ch in albIcons[i]) Destroy(ch.gameObject);
            for (int j = 0; j < g.animals.Length; j++)
            {
                int a = g.animals[j];
                float x = 12 + j * 24;
                if (a < zooCount)
                {
                    var cr = MakeCreature(albIcons[i], a, 20f, MyVariant(a));
                    cr.anchoredPosition = new Vector2(x, -13);
                }
                else
                {
                    var q = TL(albIcons[i], x - 9, 4, 18, 18, "Locked");
                    Round(q, C(238, 227, 247), 9);
                    Label(q, "?", 10, Muted, TextAnchor.MiddleCenter, 0, 0, 18, 18);
                }
            }

            bool owned = cosOwned.Contains(g.reward);
            int k = CosIndex(g.reward);
            bool on = owned && k >= 0 && Equipped(CosItems[k].kind) == g.reward;
            bool complete = GroupComplete(g, zooCount);
            albBtnImg[i].color = on ? Ink : owned ? C(238, 227, 247) : complete ? Red : C(246, 240, 250);
            albBtnTxt[i].color = on || (!owned && complete) ? Color.white : owned ? Ink : Muted;
            albBtnTxt[i].text = on ? "Снять" : owned ? "Надеть" : complete ? "Забрать" : have + "/" + g.animals.Length;
        }
    }

    void AlbumButton(int i)
    {
        var g = Album[i];
        int k = CosIndex(g.reward);
        if (k < 0) return;
        if (cosOwned.Contains(g.reward))
        {
            bool on = Equipped(CosItems[k].kind) == g.reward;
            SetCosmetic(CosItems[k].kind, on ? "" : g.reward);
            GameAudio.Click();
            RefreshAlbum();
            return;
        }
        if (!GroupComplete(g, zooCount))
        {
            GameAudio.Deny();
            Toast("Не хватает животных: " + (g.animals.Length - GroupHave(g, zooCount)));
            return;
        }
        if (albumBusy) return;
        if (netState != NetState.Online) { GameAudio.Deny(); Toast("Нет связи с сервером"); KickNet(); return; }
        StartCoroutine(ClaimAlbum(g.reward));
    }

    IEnumerator ClaimAlbum(string item)
    {
        albumBusy = true;
        long code = 0;
        NetShopState r = null;
        var req = new NetAlbumReq { id = netId, token = netToken, item = item, zoo = zooCount };
        yield return Http("POST", netUrl, "/api/album/claim", JsonUtility.ToJson(req), (c, t) => { code = c; r = Parse<NetShopState>(t); }, 20);
        albumBusy = false;
        if (code == 200 && r != null && r.ok)
        {
            SetOwned(r.owned);
            int k = CosIndex(item);
            if (k >= 0) SetCosmetic(CosItems[k].kind, item);      // сразу надеть
            GameAudio.Hatch();
            Toast("Награда альбома: " + (k >= 0 ? CosItems[k].title : item) + "!");
            Burst(new Vector2(300, 360), C(255, 205, 90), 18, menuFx);
        }
        else
        {
            GameAudio.Deny();
            Toast(code == 0 ? "Нет связи с сервером" : code == 404 ? "Обновите сервер игры" : "Не получилось, попробуйте ещё раз");
        }
        RefreshAlbum();
    }
}
