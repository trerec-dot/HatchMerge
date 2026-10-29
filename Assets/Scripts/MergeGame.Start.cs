// Стартовое меню: название, карточка игрока, кнопки «Играть», «Найти игрока», «Мой зоопарк»,
// «Настройки» и «Выход». Открывается при запуске игры и кнопкой «Меню» на нижней панели.

using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public partial class MergeGame
{
    const int TabStart = 4, TabPlayers = 5;

    RectTransform startPanel, startAvatar;
    Text startPlayTxt, startNickTxt, startIdTxt, startAccTxt;
    Image startDot;

    void BuildStartMenu()
    {
        startPanel = TL(content, 0, 0, 390, 700, "StartMenu");

        // три яйца над названием
        Egg.Make(startPanel, new Vector2(118, 112), 3, 1.15f, true);
        Egg.Make(startPanel, new Vector2(195, 96), 7, 1.55f, true);
        Egg.Make(startPanel, new Vector2(272, 112), 5, 1.15f, true);

        Label(startPanel, "Яйца-сюрпризы", 34, Ink, TextAnchor.MiddleCenter, 0, 168, 390, 48);
        Label(startPanel, "Соединяй яйца — открывай животных", 14, Muted, TextAnchor.MiddleCenter, 0, 214, 390, 22);

        // карточка игрока
        var me = TL(startPanel, 20, 256, 350, 72, "PlayerCard");
        Round(me, Color.white, 24);
        startAvatar = TL(me, 0, 0, 72, 72, "Avatar");
        startNickTxt = Label(me, "", 16, Ink, TextAnchor.MiddleLeft, 74, 12, 190, 26);
        startDot = AddImg(TL(me, 74, 45, 10, 10, "Dot"), circleSpr, Muted);
        startIdTxt = Label(me, "", 13, Muted, TextAnchor.MiddleLeft, 90, 38, 174, 24);
        // карточка игрока открывает экран аккаунта
        var acc = TL(me, 262, 18, 76, 36, "Account");
        Round(acc, C(238, 227, 247), 18);
        startAccTxt = Label(acc, "", 13, Ink, TextAnchor.MiddleCenter, 0, 0, 76, 36);
        AddBtn(me, () => SetTab(TabAccount), TabStart);
        // нажатие на аватар — выбор аватара (проверяется раньше всей карточки)
        btns.Insert(0, new Btn { rt = startAvatar, act = OpenAvatarPicker, tab = TabStart });

        // кнопки
        var play = TL(startPanel, 20, 348, 350, 66, "Play");
        Round(play, Red, 33);
        startPlayTxt = Label(play, "Играть", 24, Color.white, TextAnchor.MiddleCenter, 0, 0, 350, 66);
        AddBtn(play, () => SetTab(0), TabStart);

        MenuButton("Найти игрока", 428, () => SetTab(TabPlayers));
        MenuButton("Мой зоопарк", 494, () => SetTab(1));
        MenuButton("Настройки", 560, () => SetTab(3));

#if !UNITY_IOS && !UNITY_WEBGL
        var quit = TL(startPanel, 120, 632, 150, 40, "Quit");
        Label(quit, "Выход", 15, Muted, TextAnchor.MiddleCenter, 0, 0, 150, 40);
        AddBtn(quit, QuitGame, TabStart);
#endif
    }

    void MenuButton(string title, float y, System.Action act)
    {
        var b = TL(startPanel, 20, y, 350, 56, "MenuBtn");
        Round(b, Color.white, 28);
        Label(b, title, 18, Ink, TextAnchor.MiddleCenter, 0, 0, 350, 56);
        AddBtn(b, act, TabStart);
    }

    void RefreshStartMenu()
    {
        if (startPanel == null) return;
        startPlayTxt.text = (score > 0 || zooCount > 0) ? "Продолжить" : "Играть";
        startNickTxt.text = NickName(nickA, nickB);
        startAccTxt.text = HasAccount ? "Аккаунт" : "Войти";
        DrawAvatar(startAvatar, MyAvatar(), 46f, new Vector2(38, 36), cosFr);
        RefreshStartNet();
    }

    void RefreshStartNet()
    {
        if (startPanel == null) return;
        string id = string.IsNullOrEmpty(netId) ? "ID появится после подключения" : "ID " + netId;
        startIdTxt.text = id + "  ·  " + NetStateText();
        startDot.color = NetStateColor();
    }

    void QuitGame()
    {
        GameAudio.Click();
        Save(); PlayerPrefs.Save(); NetGoodbye();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Аватар: последнее открытое животное, а если животных ещё нет — яйцо
    static void DrawAvatar(RectTransform holder, int creature, float size, Vector2 center, string frame = null)
    {
        foreach (Transform ch in holder) Destroy(ch.gameObject);
        if (!string.IsNullOrEmpty(frame)) DrawFrame(holder, frame, size, center);   // рамка из магазина оформления
        if (creature >= 0 && creature < Zoo.Length)
        {
            var cr = MakeCreature(holder, creature, size);
            cr.anchoredPosition = new Vector2(center.x, -center.y);
        }
        else Egg.Make(holder, center, 1, size / 56f, true);
    }

    // «Назад» на Android и Esc на компьютере возвращают в стартовое меню
    static bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }
}
