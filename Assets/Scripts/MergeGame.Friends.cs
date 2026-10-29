// Друзья и профиль игрока.
// • На экране «Игроки» две вкладки: «Поиск» (по ID) и «Друзья» (список с пагинацией, кто в сети — сверху).
// • Найденного игрока можно добавить в друзья; нажатие на карточку открывает его профиль.
// • Профиль: аватар, имя, ID, статус, уровень, зоопарк, монеты, кристаллы, сколько дней в игре, друзей.
// • Список друзей хранится на сервере (привязан к ID игрока), администратор видит его на /admin.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[Serializable] class NetFriendReq { public string id, token, friend; }
[Serializable] class NetFriendList { public bool ok; public string error; public NetPlayer[] friends; }

public partial class MergeGame
{
    const int TabProfile = 7;
    const int FriendRows = 6;

    bool friendsMode;                       // вкладка «Друзья» на экране «Игроки»
    int friendsPage;
    float friendsT;
    bool friendsLoading;
    readonly List<NetPlayer> friendList = new List<NetPlayer>();
    readonly HashSet<string> friendIds = new HashSet<string>();

    RectTransform searchGroup, friendsGroup, foundAddBtn;
    readonly Image[] plModeImg = new Image[2];
    readonly Text[] plModeTxt = new Text[2];
    Image foundAddImg;
    Text foundAddTxt, friendsMsg, friendsPageTxt;
    RectTransform friendsPrev, friendsNext;

    class FriendRow { public RectTransform rt, avatar; public Text nick, status, level; public Image dot; }
    readonly FriendRow[] rows = new FriendRow[FriendRows];

    // профиль
    RectTransform profilePanel, profAvatar, profZoo;
    Text profNick, profId, profStatus, profMutual, profAddTxt, profStats, profMsg;
    Image profDot, profAddImg;
    NetPlayer profile;
    string profileId;
    float profileT, removeArm;

    // ============================================================
    //  Построение интерфейса (вызывается из BuildPlayers)
    // ============================================================
    void BuildFriendsUi()
    {
        // переключатель «Поиск / Друзья» рядом с заголовком
        string[] modes = { "Поиск", "Друзья" };
        for (int i = 0; i < 2; i++)
        {
            int m = i;
            var pill = TL(playersPanel, 186 + i * 94, 84, 90, 32, "PlMode");
            plModeImg[i] = Round(pill, Color.white, 16);
            plModeTxt[i] = Label(pill, modes[i], 13, Ink, TextAnchor.MiddleCenter, 0, 0, 90, 32);
            AddBtn(pill, () => SetFriendsMode(m == 1), TabPlayers);
        }

        // кнопка «В друзья» на карточке найденного игрока — до карточки, чтобы нажатие попало в неё
        foundAddBtn = TL(foundInfo, 248, 12, 90, 30, "AddFriend");
        foundAddImg = Round(foundAddBtn, Red, 15);
        foundAddTxt = Label(foundAddBtn, "В друзья", 12, Color.white, TextAnchor.MiddleCenter, 0, 0, 90, 30);
        AddBtn(foundAddBtn, () => { if (shownId != null) ToggleFriend(shownId, false); }, TabPlayers);
        foundNick.rectTransform.sizeDelta = new Vector2(160, 26);
        AddBtn(foundCard, () => { if (shownId != null && foundInfo.gameObject.activeSelf) OpenProfile(shownId); }, TabPlayers);

        // --- вкладка «Друзья» ---
        friendsGroup = TL(playersPanel, 0, 0, 390, 700, "Friends");
        for (int i = 0; i < FriendRows; i++)
        {
            int idx = i;
            var r = new FriendRow();
            r.rt = TL(friendsGroup, 20, 222 + i * 64, 350, 58, "Friend");
            Round(r.rt, Color.white, 20);
            r.avatar = TL(r.rt, 0, 0, 60, 58, "Avatar");
            r.nick = Label(r.rt, "", 15, Ink, TextAnchor.MiddleLeft, 60, 8, 200, 24);
            r.dot = AddImg(TL(r.rt, 60, 37, 9, 9, "Dot"), circleSpr, Muted);
            r.status = Label(r.rt, "", 12, Muted, TextAnchor.MiddleLeft, 74, 30, 190, 22);
            r.level = Label(r.rt, "", 13, Ink, TextAnchor.MiddleRight, 250, 0, 86, 58);
            AddBtn(r.rt, () => OpenFriendRow(idx), TabPlayers);
            rows[i] = r;
        }
        friendsMsg = WrapLabel(friendsGroup, "", 15, Muted, TextAnchor.UpperCenter, 30, 250, 330, 80);

        friendsPrev = TL(friendsGroup, 20, 612, 80, 36, "Prev");
        Round(friendsPrev, Color.white, 18);
        Label(friendsPrev, "‹ Назад", 13, Ink, TextAnchor.MiddleCenter, 0, 0, 80, 36);
        AddBtn(friendsPrev, () => { friendsPage--; GameAudio.Click(); RefreshFriendList(); }, TabPlayers);
        friendsNext = TL(friendsGroup, 290, 612, 80, 36, "Next");
        Round(friendsNext, Color.white, 18);
        Label(friendsNext, "Дальше ›", 13, Ink, TextAnchor.MiddleCenter, 0, 0, 80, 36);
        AddBtn(friendsNext, () => { friendsPage++; GameAudio.Click(); RefreshFriendList(); }, TabPlayers);
        friendsPageTxt = Label(friendsGroup, "", 13, Muted, TextAnchor.MiddleCenter, 100, 612, 190, 36);

        // элементы поиска собираем в группу, чтобы прятать их на вкладке «Друзья»
        searchGroup = TL(playersPanel, 0, 0, 390, 700, "Search");
        var move = new List<Transform>();
        foreach (Transform ch in playersPanel)
        {
            string n = ch.name;
            if (n == "IdBox" || n == "Find" || n == "Key" || n == "Found") move.Add(ch);
        }
        foreach (var ch in move) ch.SetParent(searchGroup, false);

        BuildProfile();
        ApplyFriendsMode();
    }

    void BuildProfile()
    {
        profilePanel = TL(content, 0, 0, 390, 700, "ProfilePanel");
        var back = TL(profilePanel, 20, 82, 120, 36, "Back");
        Label(back, "← Назад", 16, Red, TextAnchor.MiddleLeft, 0, 0, 120, 36);
        AddBtn(back, () => SetTab(TabPlayers), TabProfile);

        var card = TL(profilePanel, 20, 124, 350, 130, "Card");
        Round(card, Color.white, 24);
        profAvatar = TL(card, 0, 0, 110, 130, "Avatar");
        profNick = Label(card, "", 19, Ink, TextAnchor.MiddleLeft, 110, 16, 230, 30);
        profId = Label(card, "", 14, Muted, TextAnchor.MiddleLeft, 110, 46, 230, 22);
        profDot = AddImg(TL(card, 110, 79, 10, 10, "Dot"), circleSpr, Muted);
        profStatus = Label(card, "", 14, Muted, TextAnchor.MiddleLeft, 126, 72, 214, 24);
        profMutual = Label(card, "", 13, C(60, 150, 110), TextAnchor.MiddleLeft, 110, 98, 230, 22);

        var add = TL(profilePanel, 20, 266, 350, 52, "Friend");
        profAddImg = Round(add, Red, 26);
        profAddTxt = Label(add, "", 17, Color.white, TextAnchor.MiddleCenter, 0, 0, 350, 52);
        AddBtn(add, ProfileFriendButton, TabProfile);
        profMsg = WrapLabel(profilePanel, "", 13, Muted, TextAnchor.UpperCenter, 20, 322, 350, 20);

        var stats = TL(profilePanel, 20, 346, 350, 112, "Stats");
        Round(stats, Color.white, 24);
        profStats = WrapLabel(stats, "", 15, Ink, TextAnchor.MiddleLeft, 18, 0, 314, 112);
        profStats.lineSpacing = 1.15f;

        var zooCard = TL(profilePanel, 20, 470, 350, 150, "Zoo");
        Round(zooCard, Color.white, 24);
        Label(zooCard, "Зоопарк", 15, Ink, TextAnchor.MiddleLeft, 18, 8, 200, 28);
        profZoo = TL(zooCard, 0, 0, 350, 150, "Animals");
    }

    // ============================================================
    //  Вкладки «Поиск / Друзья»
    // ============================================================
    void SetFriendsMode(bool on)
    {
        if (friendsMode == on) return;
        friendsMode = on;
        GameAudio.Click();
        ApplyFriendsMode();
        if (on) { friendsPage = 0; StartCoroutine(LoadFriends(true)); }
    }

    void ApplyFriendsMode()
    {
        if (searchGroup == null) return;
        searchGroup.gameObject.SetActive(!friendsMode);
        friendsGroup.gameObject.SetActive(friendsMode);
        for (int i = 0; i < 2; i++)
        {
            bool sel = (i == 1) == friendsMode;
            plModeImg[i].color = sel ? Ink : Color.white;
            plModeTxt[i].color = sel ? Color.white : Ink;
        }
        RefreshFriendList();
    }

    void OnFriendsOpened()
    {
        ApplyFriendsMode();
        friendsT = 0f;
        StartCoroutine(LoadFriends(friendsMode));
    }

    // ============================================================
    //  Сервер
    // ============================================================
    IEnumerator LoadFriends(bool showLoading)
    {
        if (netState != NetState.Online || string.IsNullOrEmpty(netId))
        {
            if (friendsMode) { friendsMsg.text = "Нет связи с сервером"; HideRows(); }
            yield break;
        }
        if (friendsLoading) yield break;
        friendsLoading = true;
        if (showLoading && friendList.Count == 0) { friendsMsg.text = "Загружаем друзей…"; HideRows(); }

        long code = 0;
        NetFriendList r = null;
        yield return Http("POST", netUrl, "/api/friends/list", JsonUtility.ToJson(new NetAuthReq { id = netId, token = netToken }),
            (c, t) => { code = c; r = Parse<NetFriendList>(t); });
        friendsLoading = false;

        if (code == 200 && r != null && r.friends != null)
        {
            friendList.Clear();
            friendIds.Clear();
            foreach (var f in r.friends) if (f != null && !string.IsNullOrEmpty(f.id)) { friendList.Add(f); friendIds.Add(f.id); }
        }
        else if (code == 0 && friendsMode) friendsMsg.text = "Нет связи с сервером";
        RefreshFriendList();
        RefreshFoundFriendButton();
        if (tab == TabProfile) RefreshProfile();
    }

    void ToggleFriend(string id, bool fromProfile)
    {
        if (string.IsNullOrEmpty(id) || id == netId) return;
        if (netState != NetState.Online) { GameAudio.Deny(); Toast("Нет связи с сервером"); return; }
        bool remove = friendIds.Contains(id);
        if (remove && fromProfile && removeArm <= 0f)
        {
            removeArm = 3f;
            GameAudio.Click();
            RefreshProfile();
            return;
        }
        removeArm = 0f;
        StartCoroutine(DoToggleFriend(id, remove));
    }

    IEnumerator DoToggleFriend(string id, bool remove)
    {
        long code = 0;
        NetAccountResp r = null;
        var req = new NetFriendReq { id = netId, token = netToken, friend = id };
        yield return Http("POST", netUrl, remove ? "/api/friends/remove" : "/api/friends/add", JsonUtility.ToJson(req),
            (c, t) => { code = c; r = Parse<NetAccountResp>(t); });

        if (code == 200)
        {
            if (remove) { friendIds.Remove(id); friendList.RemoveAll(f => f.id == id); GameAudio.Click(); Toast("Убрали из друзей"); }
            else { friendIds.Add(id); GameAudio.Buy(); Toast("Добавлен в друзья!"); }
            StartCoroutine(LoadFriends(false));
        }
        else
        {
            GameAudio.Deny();
            string err = r != null ? r.error : null;
            Toast(err == "too_many_friends" ? "Друзей может быть не больше 100"
                : err == "not_found" ? "Игрок не найден"
                : code == 0 ? "Нет связи с сервером" : "Не получилось, попробуйте ещё раз");
        }
        RefreshFoundFriendButton();
        if (tab == TabProfile) RefreshProfile();
    }

    // ============================================================
    //  Список друзей
    // ============================================================
    void HideRows()
    {
        foreach (var r in rows) r.rt.gameObject.SetActive(false);
        friendsPrev.gameObject.SetActive(false);
        friendsNext.gameObject.SetActive(false);
        friendsPageTxt.text = "";
    }

    void RefreshFriendList()
    {
        if (friendsGroup == null || !friendsMode) return;
        int pages = Mathf.Max(1, (friendList.Count + FriendRows - 1) / FriendRows);
        friendsPage = Mathf.Clamp(friendsPage, 0, pages - 1);

        if (friendList.Count == 0)
        {
            HideRows();
            if (!friendsLoading && netState == NetState.Online)
                friendsMsg.text = "Пока нет друзей.\nНайдите игрока по ID на вкладке «Поиск» и нажмите «В друзья».";
            return;
        }
        friendsMsg.text = "";
        for (int i = 0; i < FriendRows; i++)
        {
            int k = friendsPage * FriendRows + i;
            var row = rows[i];
            bool has = k < friendList.Count;
            row.rt.gameObject.SetActive(has);
            if (!has) continue;
            var p = friendList[k];
            row.nick.text = NickName(p.nickA, p.nickB);
            row.dot.color = p.online ? C(110, 200, 160) : C(200, 190, 210);
            row.status.text = (p.online ? "В сети" : "Был " + AgoText(p.lastSeenSec)) + (p.mutual ? " · дружим" : "");
            row.level.text = "Ур. " + p.level;
            DrawAvatar(row.avatar, p.avatar, 36f, new Vector2(32, 29), p.frame);
        }
        friendsPrev.gameObject.SetActive(friendsPage > 0);
        friendsNext.gameObject.SetActive(friendsPage < pages - 1);
        friendsPageTxt.text = pages > 1 ? "Страница " + (friendsPage + 1) + " из " + pages : "Друзей: " + friendList.Count;
    }

    void OpenFriendRow(int i)
    {
        int k = friendsPage * FriendRows + i;
        if (k < friendList.Count) OpenProfile(friendList[k].id, friendList[k]);
    }

    void RefreshFoundFriendButton()
    {
        if (foundAddBtn == null) return;
        bool me = shownId != null && shownId == netId;
        foundAddBtn.gameObject.SetActive(shownId != null && !me);
        bool isFriend = shownId != null && friendIds.Contains(shownId);
        foundAddImg.color = isFriend ? C(238, 227, 247) : Red;
        foundAddTxt.color = isFriend ? Ink : Color.white;
        foundAddTxt.text = isFriend ? "В друзьях" : "В друзья";
    }

    // ============================================================
    //  Профиль
    // ============================================================
    void OpenProfile(string id, NetPlayer known = null)
    {
        profileId = id;
        profile = known;
        removeArm = 0f;
        profMsg.text = "";
        GameAudio.Click();
        SetTab(TabProfile);
        StartCoroutine(FetchProfile(id));
    }

    IEnumerator FetchProfile(string id)
    {
        if (profile == null) { profNick.text = "Загружаем…"; RefreshProfile(); }
        long code = 0; string text = null;
        yield return Http("GET", netUrl, "/api/player/" + id, null, (c, t) => { code = c; text = t; });
        if (id != profileId) yield break;
        if (code == 200)
        {
            var p = Parse<NetPlayer>(text);
            if (p != null && p.found)
            {
                bool mutual = profile != null && profile.mutual;
                profile = p;
                profile.mutual = mutual || (friendList.Find(f => f.id == id)?.mutual ?? false);
            }
        }
        else if (code == 404) { profile = null; profNick.text = "Игрок не найден"; }
        else if (profile == null) profNick.text = "Нет связи с сервером";
        RefreshProfile();
    }

    void RefreshProfile()
    {
        if (profilePanel == null || tab != TabProfile) return;
        var p = profile;
        bool me = profileId == netId;
        bool isFriend = profileId != null && friendIds.Contains(profileId);

        profAddImg.transform.parent.gameObject.SetActive(p != null && !me);
        profAddImg.color = isFriend ? (removeArm > 0f ? Red : C(238, 227, 247)) : Red;
        profAddTxt.color = isFriend && removeArm <= 0f ? Ink : Color.white;
        profAddTxt.text = !isFriend ? "Добавить в друзья"
            : removeArm > 0f ? "Нажмите ещё раз, чтобы убрать" : "Вы друзья  ·  убрать из друзей";

        if (p == null)
        {
            foreach (Transform ch in profAvatar) Destroy(ch.gameObject);
            foreach (Transform ch in profZoo) Destroy(ch.gameObject);
            profId.text = profileId != null ? "ID " + profileId : "";
            profStatus.text = profMutual.text = profStats.text = "";
            profDot.gameObject.SetActive(false);
            return;
        }

        profNick.text = NickName(p.nickA, p.nickB) + (me ? " (это вы)" : "");
        profId.text = "ID " + p.id;
        profDot.gameObject.SetActive(true);
        profDot.color = p.online ? C(110, 200, 160) : C(200, 190, 210);
        profStatus.text = p.online ? "В сети" : "Не в сети · был " + AgoText(p.lastSeenSec);
        profMutual.text = isFriend && p.mutual ? "Вы дружите взаимно" : "";
        DrawAvatar(profAvatar, p.avatar, 76f, new Vector2(56, 65), p.frame);

        int total = p.zooTotal > 0 ? p.zooTotal : Zoo.Length;
        profStats.text =
            "Уровень: " + p.level + "\n" +
            "Монеты: " + Fmt(p.score) + "   ·   Кристаллы: " + p.gems + "\n" +
            "Лучшее яйцо на поле: " + (p.bestEgg > 0 ? p.bestEgg.ToString() : "—") + "\n" +
            "В игре: " + DaysText(p.sinceDays) + "   ·   Друзей: " + p.friends;

        foreach (Transform ch in profZoo) Destroy(ch.gameObject);
        Label(profZoo, p.zoo + " из " + total, 13, Muted, TextAnchor.MiddleRight, 200, 8, 132, 28);
        // 12 мест: последние открытые животные и следующие за ними
        int first = Mathf.Clamp(p.zoo - 12, 0, Mathf.Max(0, Zoo.Length - 12));
        for (int i = first; i < Mathf.Min(Zoo.Length, first + 12); i++)
        {
            int k = i - first;
            float cx = 34 + (k % 6) * 56.4f, cy = 64 + (k / 6) * 50;
            if (i < p.zoo)
            {
                var cr = MakeCreature(profZoo, i, 34f);
                cr.anchoredPosition = new Vector2(cx, -cy);
            }
            else
            {
                var q = TL(profZoo, cx - 15, cy - 15, 30, 30, "Locked");
                Round(q, C(238, 227, 247), 15);
                Label(q, "?", 14, Muted, TextAnchor.MiddleCenter, 0, 0, 30, 30);
            }
        }
    }

    static string DaysText(int d)
    {
        if (d <= 0) return "с сегодняшнего дня";
        int m10 = d % 10, m100 = d % 100;
        string w = m10 == 1 && m100 != 11 ? "день" : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "дня" : "дней";
        return d + " " + w;
    }

    void ProfileFriendButton()
    {
        if (profileId != null) ToggleFriend(profileId, true);
    }

    // вызывается каждый кадр из Update
    void UpdateFriends(float dt)
    {
        if (removeArm > 0f)
        {
            removeArm -= dt;
            if (removeArm <= 0f && tab == TabProfile) RefreshProfile();
        }
        if (tab == TabPlayers && friendsMode)
        {
            friendsT += dt;
            if (friendsT >= 15f) { friendsT = 0f; StartCoroutine(LoadFriends(false)); }
        }
        if (tab == TabProfile && profileId != null)
        {
            profileT += dt;
            if (profileT >= 10f) { profileT = 0f; StartCoroutine(FetchProfile(profileId)); }
        }
    }
}
