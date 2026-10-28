// Экран «Игроки»: мой профиль и ID, поиск другого игрока по ID и его статус
// (в сети / не в сети, уровень, животные, монеты, кристаллы).
// ID вводится экранной цифровой клавиатурой (на компьютере — и цифрами на клавиатуре, через OnGUI).

using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    // Имена собираются из двух списков — свободного ввода текста нет, это безопаснее для детской игры.
    static readonly string[] NickA =
    {
        "Весёлый", "Смелый", "Быстрый", "Хитрый", "Добрый", "Сонный", "Звёздный", "Солнечный",
        "Пушистый", "Ловкий", "Храбрый", "Шустрый", "Радужный", "Мятный", "Ягодный", "Облачный",
    };
    static readonly string[] NickB =
    {
        "Котик", "Зайчик", "Ёжик", "Мишка", "Пингвин", "Дракончик", "Енотик", "Лисёнок",
        "Совёнок", "Тигрёнок", "Слонёнок", "Бельчонок", "Утёнок", "Пёсик", "Хомячок", "Единорожек",
    };

    static string NickName(int a, int b) =>
        NickA[Mathf.Clamp(a, 0, NickA.Length - 1)] + " " + NickB[Mathf.Clamp(b, 0, NickB.Length - 1)];

    const int IdLength = 6;
    const float FoundRefresh = 10f;

    RectTransform playersPanel, myAvatar, foundCard, foundInfo, foundAvatar;
    Text myNickTxt, myIdTxt, myNetTxt, searchTxt, foundMsg, foundNick, foundStatus, foundProg, foundExtra;
    Image myDot, foundDot, findImg;
    string searchId = "";
    string shownId;          // ID игрока, чья карточка сейчас показана
    float foundT;

    void BuildPlayers()
    {
        playersPanel = TL(content, 0, 0, 390, 700, "PlayersPanel");
        Label(playersPanel, "Игроки", 22, Ink, TextAnchor.MiddleLeft, 20, 82, 260, 36);

        // --- мой профиль ---
        var me = TL(playersPanel, 20, 124, 350, 84, "Me");
        Round(me, Color.white, 22);
        myAvatar = TL(me, 0, 0, 80, 84, "Avatar");
        myNickTxt = Label(me, "", 16, Ink, TextAnchor.MiddleLeft, 80, 10, 170, 26);
        myIdTxt = Label(me, "", 14, Ink, TextAnchor.MiddleLeft, 80, 36, 170, 22);
        myDot = AddImg(TL(me, 80, 63, 9, 9, "Dot"), circleSpr, Muted);
        myNetTxt = Label(me, "", 12, Muted, TextAnchor.MiddleLeft, 94, 57, 160, 20);
        var rn = TL(me, 252, 24, 86, 36, "Rename");
        Round(rn, C(238, 227, 247), 18);
        Label(rn, "Другое имя", 12, Ink, TextAnchor.MiddleCenter, 0, 0, 86, 36);
        AddBtn(rn, NextNick, TabPlayers);

        // --- поле ID и кнопка поиска ---
        var box = TL(playersPanel, 20, 222, 236, 52, "IdBox");
        Round(box, Color.white, 22);
        searchTxt = Label(box, "", 22, Ink, TextAnchor.MiddleCenter, 0, 0, 236, 52);
        var find = TL(playersPanel, 266, 222, 104, 52, "Find");
        findImg = Round(find, Red, 26);
        Label(find, "Найти", 18, Color.white, TextAnchor.MiddleCenter, 0, 0, 104, 52);
        AddBtn(find, DoSearch, TabPlayers);

        // --- цифровая клавиатура ---
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Сброс", "0", "Удалить" };
        const float kw = 111.3f, kh = 42f, gap = 8f;
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            var key = TL(playersPanel, 20 + (i % 3) * (kw + gap), 284 + (i / 3) * (kh + gap), kw, kh, "Key");
            bool digit = k.Length == 1;
            Round(key, digit ? Color.white : C(238, 227, 247), 18);
            Label(key, k, digit ? 20 : 14, Ink, TextAnchor.MiddleCenter, 0, 0, kw, kh);
            System.Action act;
            if (digit) act = () => TypeDigit(k[0]);
            else if (k == "Сброс") act = () => { searchId = ""; GameAudio.Click(); RefreshSearchBox(); };
            else act = Backspace;
            AddBtn(key, act, TabPlayers);
        }

        // --- карточка найденного игрока ---
        foundCard = TL(playersPanel, 20, 488, 350, 124, "Found");
        Round(foundCard, Color.white, 24);
        foundMsg = Label(foundCard, "", 15, Muted, TextAnchor.MiddleCenter, 16, 0, 318, 124);
        foundInfo = TL(foundCard, 0, 0, 350, 124, "Info");
        foundAvatar = TL(foundInfo, 0, 0, 84, 124, "Avatar");
        foundNick = Label(foundInfo, "", 17, Ink, TextAnchor.MiddleLeft, 84, 12, 250, 26);
        foundDot = AddImg(TL(foundInfo, 84, 46, 10, 10, "Dot"), circleSpr, Muted);
        foundStatus = Label(foundInfo, "", 13, Muted, TextAnchor.MiddleLeft, 100, 40, 234, 22);
        foundProg = Label(foundInfo, "", 13, Ink, TextAnchor.MiddleLeft, 84, 66, 250, 22);
        foundExtra = Label(foundInfo, "", 13, Muted, TextAnchor.MiddleLeft, 84, 90, 250, 22);

        ShowFoundMsg("Введите ID игрока, чтобы увидеть его статус");
        RefreshSearchBox();
        BuildFriendsUi();
    }

    void OnPlayersOpened()
    {
        RefreshMyCard();
        RefreshSearchBox();
        KickNet();                                   // обновить свой статус на сервере
        if (shownId != null) StartCoroutine(FetchPlayer(shownId, false));
        foundT = 0f;
        OnFriendsOpened();
    }

    void RefreshMyCard()
    {
        if (playersPanel == null) return;
        myNickTxt.text = NickName(nickA, nickB);
        myIdTxt.text = string.IsNullOrEmpty(netId) ? "ID: —" : "Мой ID: " + netId;
        myNetTxt.text = NetStateText();
        myDot.color = NetStateColor();
        DrawAvatar(myAvatar, zooCount - 1, 48f, new Vector2(42, 42));
    }

    void RefreshSearchBox()
    {
        bool empty = searchId.Length == 0;
        searchTxt.text = empty ? "Введите ID" : searchId;
        searchTxt.fontSize = empty ? 16 : 24;
        searchTxt.color = empty ? Muted : Ink;
        findImg.color = searchId.Length == IdLength ? Red : C(216, 201, 190);
    }

    void TypeDigit(char c)
    {
        if (searchId.Length >= IdLength) { GameAudio.Deny(); return; }
        if (searchId.Length == 0 && c == '0') { GameAudio.Deny(); return; }   // ID не начинается с нуля
        searchId += c;
        GameAudio.Click();
        RefreshSearchBox();
    }

    void Backspace()
    {
        if (searchId.Length == 0) return;
        searchId = searchId.Substring(0, searchId.Length - 1);
        GameAudio.Click();
        RefreshSearchBox();
    }

    void NextNick()
    {
        int a = nickA, b = nickB;
        while (a == nickA && b == nickB) { a = Random.Range(0, NickA.Length); b = Random.Range(0, NickB.Length); }
        nickA = a; nickB = b;
        PlayerPrefs.SetInt("net_nickA", nickA);
        PlayerPrefs.SetInt("net_nickB", nickB);
        GameAudio.Click();
        RefreshMyCard();
        KickNet();
    }

    void DoSearch()
    {
        if (searchId.Length != IdLength) { GameAudio.Deny(); ShowFoundMsg("ID состоит из " + IdLength + " цифр"); return; }
        if (netState != NetState.Online) { GameAudio.Deny(); ShowFoundMsg("Нет связи с сервером. Проверьте, что сервер запущен"); KickNet(); return; }
        GameAudio.Click();
        shownId = searchId;
        foundT = 0f;
        StartCoroutine(FetchPlayer(searchId, true));
    }

    void ShowFoundMsg(string s)
    {
        foundInfo.gameObject.SetActive(false);
        foundMsg.gameObject.SetActive(true);
        foundMsg.text = s;
    }

    void ShowFound(NetPlayer p)
    {
        foundMsg.gameObject.SetActive(false);
        foundInfo.gameObject.SetActive(true);
        bool me = p.id == netId;
        foundNick.text = NickName(p.nickA, p.nickB) + (me ? " (это вы)" : "");
        foundDot.color = p.online ? C(110, 200, 160) : C(200, 190, 210);
        foundStatus.text = p.online ? "В сети" : "Не в сети · был " + AgoText(p.lastSeenSec);
        int total = p.zooTotal > 0 ? p.zooTotal : Zoo.Length;
        foundProg.text = "Уровень " + p.level + " · животных " + p.zoo + " из " + total;
        foundExtra.text = "Монеты " + Fmt(p.score) + " · кристаллы " + p.gems;
        DrawAvatar(foundAvatar, p.avatar, 52f, new Vector2(44, 62));
        RefreshFoundFriendButton();
    }

    static string AgoText(int sec)
    {
        if (sec < 60) return "только что";
        if (sec < 3600) return (sec / 60) + " мин назад";
        if (sec < 86400) return (sec / 3600) + " ч назад";
        return (sec / 86400) + " дн назад";
    }

    // вызывается каждый кадр из Update
    void UpdatePlayers(float dt)
    {
        if (tab != TabPlayers) return;

        // статус найденного игрока обновляется сам
        if (shownId != null && netState == NetState.Online && foundInfo.gameObject.activeSelf)
        {
            foundT += dt;
            if (foundT >= FoundRefresh) { foundT = 0f; StartCoroutine(FetchPlayer(shownId, false)); }
        }
    }

    // цифры, Backspace и Enter с клавиатуры компьютера (вызывается из OnGUI)
    bool PlayersKey(Event e)
    {
        if (e.keyCode == KeyCode.Backspace) Backspace();
        else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) DoSearch();
        else if (e.character >= '0' && e.character <= '9') TypeDigit(e.character);
        else return false;
        return true;
    }
}
