// Выбор аватара. Доступны яйцо и все уже открытые животные из зоопарка; закрытые показаны «?».
// «Авто» — аватаром всегда будет последнее открытое животное (так было раньше).
// Выбор хранится на устройстве, попадает в облачное сохранение аккаунта и отправляется на сервер,
// поэтому друзья и администратор видят выбранный аватар.
// Открыть: нажать на свой аватар на экране «Игроки» или в стартовом меню.

using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    const int TabAvatar = 8;
    const int AvatarAuto = -2, AvatarEgg = -1;

    int avatarSel = AvatarAuto;
    int avatarReturnTab = TabStart;

    RectTransform avatarPanel;
    readonly RectTransform[] avCells = new RectTransform[9];
    readonly RectTransform[] avHolders = new RectTransform[9];
    readonly Image[] avBg = new Image[9];
    readonly Text[] avName = new Text[9];
    Image avAutoImg;
    Text avAutoTxt, avMsg;

    // аватар, который видят все: выбранный, если он открыт, иначе последнее открытое животное
    int MyAvatar()
    {
        if (avatarSel == AvatarEgg) return AvatarEgg;
        if (avatarSel >= 0 && avatarSel < zooCount) return avatarSel;
        return zooCount - 1;
    }

    void BuildAvatarPicker()
    {
        avatarSel = Mathf.Clamp(PlayerPrefs.GetInt("avatar_sel", AvatarAuto), AvatarAuto, Zoo.Length - 1);

        avatarPanel = TL(content, 0, 0, 390, 700, "AvatarPanel");
        var back = TL(avatarPanel, 20, 82, 120, 36, "Back");
        Label(back, "← Назад", 16, Red, TextAnchor.MiddleLeft, 0, 0, 120, 36);
        AddBtn(back, () => SetTab(avatarReturnTab), TabAvatar);
        Label(avatarPanel, "Выберите аватар", 22, Ink, TextAnchor.MiddleLeft, 20, 116, 350, 34);
        WrapLabel(avatarPanel, "Новые животные из зоопарка становятся доступны, когда вылупятся",
            13, Muted, TextAnchor.UpperLeft, 20, 150, 350, 36);

        for (int i = 0; i < 9; i++)
        {
            int opt = i - 1;                      // -1 — яйцо, 0..7 — животные
            float x = 20 + (i % 3) * 120, y = 192 + (i / 3) * 122;
            var cell = TL(avatarPanel, x, y, 110, 114, "Avatar");
            avBg[i] = Round(cell, Color.white, 22);
            avHolders[i] = TL(cell, 0, 0, 110, 80, "Pic");
            avName[i] = WrapLabel(cell, "", 11, Ink, TextAnchor.UpperCenter, 4, 80, 102, 32);
            avCells[i] = cell;
            AddBtn(cell, () => ChooseAvatar(opt), TabAvatar);
        }

        var auto = TL(avatarPanel, 20, 562, 350, 46, "Auto");
        avAutoImg = Round(auto, Color.white, 23);
        avAutoTxt = Label(auto, "Авто: последнее открытое животное", 14, Ink, TextAnchor.MiddleCenter, 0, 0, 350, 46);
        AddBtn(auto, () => ChooseAvatar(AvatarAuto), TabAvatar);
        avMsg = Label(avatarPanel, "", 13, Muted, TextAnchor.MiddleCenter, 20, 612, 350, 24);
    }

    void OpenAvatarPicker()
    {
        if (tab == TabAvatar) return;
        avatarReturnTab = tab;
        avMsg.text = "";
        GameAudio.Click();
        SetTab(TabAvatar);
    }

    void RefreshAvatarPicker()
    {
        if (avatarPanel == null) return;
        int current = MyAvatar();
        for (int i = 0; i < 9; i++)
        {
            int opt = i - 1;
            bool open = opt == AvatarEgg || opt < zooCount;
            bool sel = avatarSel != AvatarAuto && opt == current;
            avBg[i].color = sel ? C(244, 236, 252) : open ? Color.white : C(247, 240, 232);

            foreach (Transform ch in avHolders[i]) Destroy(ch.gameObject);
            if (open)
            {
                DrawAvatar(avHolders[i], opt, 56f, new Vector2(55, 44));
                avName[i].text = opt == AvatarEgg ? "Яйцо" : Zoo[opt].name;
                avName[i].color = Ink;
            }
            else
            {
                var q = TL(avHolders[i], 33, 18, 44, 44, "Locked");
                Round(q, C(238, 227, 247), 22);
                Label(q, "?", 20, Muted, TextAnchor.MiddleCenter, 0, 0, 44, 44);
                avName[i].text = "Яйцо уровня " + Zoo[opt].target;
                avName[i].color = Muted;
            }
            if (sel)
            {
                // отметка выбранного
                var mark = TL(avHolders[i], 82, 6, 22, 22, "Sel");
                AddImg(mark, circleSpr, C(110, 200, 160));
                AddImg(CC(mark, 0, 0, 8, 8, "Dot"), circleSpr, Color.white);
            }
        }
        bool auto = avatarSel == AvatarAuto;
        avAutoImg.color = auto ? Ink : Color.white;
        avAutoTxt.color = auto ? Color.white : Ink;
    }

    void ChooseAvatar(int opt)
    {
        if (opt >= 0 && opt >= zooCount)
        {
            GameAudio.Deny();
            avMsg.text = "Это животное ещё не вылупилось";
            return;
        }
        SetAvatarSel(opt);
        GameAudio.Buy();
        avMsg.text = opt == AvatarAuto ? "Аватар будет меняться на новое животное" : "Аватар выбран!";
        RefreshAvatarPicker();
        Save();          // в облачное сохранение аккаунта
        KickNet();       // сразу сообщить серверу — друзья увидят новый аватар
    }

    void SetAvatarSel(int opt)
    {
        avatarSel = Mathf.Clamp(opt, AvatarAuto, Zoo.Length - 1);
        PlayerPrefs.SetInt("avatar_sel", avatarSel);
        RefreshMyCard();
        if (tab == TabStart) RefreshStartMenu();
    }
}
