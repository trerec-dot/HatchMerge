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
    const int AvatarCells = 12;          // ячеек на странице; варианты: яйцо + все животные
    static int AvatarPages => (Zoo.Length + 1 + AvatarCells - 1) / AvatarCells;
    int avPage;
    Text avPageTxt;
    int AvOpt(int cell) => avPage * AvatarCells + cell - 1;      // -1 — яйцо, 0.. — животные
    readonly RectTransform[] avCells = new RectTransform[AvatarCells];
    readonly RectTransform[] avHolders = new RectTransform[AvatarCells];
    readonly Image[] avBg = new Image[AvatarCells];
    readonly Text[] avName = new Text[AvatarCells];
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

        for (int i = 0; i < AvatarCells; i++)
        {
            int c = i;
            float x = 20 + (i % 4) * 89, y = 190 + (i / 4) * 92;
            var cell = TL(avatarPanel, x, y, 83, 86, "Avatar");
            avBg[i] = Round(cell, Color.white, 18);
            avHolders[i] = TL(cell, 0, 0, 83, 58, "Pic");
            avName[i] = WrapLabel(cell, "", 9, Ink, TextAnchor.UpperCenter, 2, 58, 79, 28);
            avCells[i] = cell;
            AddBtn(cell, () => { int opt = AvOpt(c); if (opt < Zoo.Length) ChooseAvatar(opt); }, TabAvatar);
        }

        avPageTxt = Label(avatarPanel, "", 14, Muted, TextAnchor.MiddleCenter, 145, 470, 100, 44);
        for (int d = -1; d <= 1; d += 2)
        {
            int dir = d;
            var arrow = TL(avatarPanel, d < 0 ? 85 : 251, 470, 54, 44, "Page");
            Round(arrow, Color.white, 22);
            Label(arrow, d < 0 ? "‹" : "›", 26, Ink, TextAnchor.MiddleCenter, 0, -2, 54, 44);
            AddBtn(arrow, () => { GameAudio.Click(); avPage = (avPage + dir + AvatarPages) % AvatarPages; RefreshAvatarPicker(); }, TabAvatar);
        }

        var auto = TL(avatarPanel, 20, 562, 350, 44, "Auto");
        avAutoImg = Round(auto, Color.white, 23);
        avAutoTxt = Label(auto, "Авто: последнее открытое животное", 14, Ink, TextAnchor.MiddleCenter, 0, 0, 350, 46);
        AddBtn(auto, () => ChooseAvatar(AvatarAuto), TabAvatar);
        avMsg = Label(avatarPanel, "", 13, Muted, TextAnchor.MiddleCenter, 20, 610, 350, 24);
    }

    void OpenAvatarPicker()
    {
        if (tab == TabAvatar) return;
        avatarReturnTab = tab;
        avMsg.text = "";
        avPage = (Mathf.Max(MyAvatar(), AvatarEgg) + 1) / AvatarCells;     // страница с текущим аватаром
        GameAudio.Click();
        SetTab(TabAvatar);
    }

    void RefreshAvatarPicker()
    {
        if (avatarPanel == null) return;
        int current = MyAvatar();
        avPage = Mathf.Clamp(avPage, 0, AvatarPages - 1);
        avPageTxt.text = (avPage + 1) + " / " + AvatarPages;
        for (int i = 0; i < AvatarCells; i++)
        {
            int opt = AvOpt(i);
            avCells[i].gameObject.SetActive(opt < Zoo.Length);
            if (opt >= Zoo.Length) continue;
            bool open = opt == AvatarEgg || opt < zooCount;
            bool sel = avatarSel != AvatarAuto && opt == current;
            avBg[i].color = sel ? C(244, 236, 252) : open ? Color.white : C(247, 240, 232);

            foreach (Transform ch in avHolders[i]) Destroy(ch.gameObject);
            if (open)
            {
                DrawAvatar(avHolders[i], opt, 40f, new Vector2(41.5f, 31));
                avName[i].text = opt == AvatarEgg ? "Яйцо" : Zoo[opt].name;
                avName[i].color = Ink;
            }
            else
            {
                var q = TL(avHolders[i], 25.5f, 13, 32, 32, "Locked");
                Round(q, C(238, 227, 247), 16);
                Label(q, "?", 16, Muted, TextAnchor.MiddleCenter, 0, 0, 32, 32);
                avName[i].text = "Яйцо уровня " + Zoo[opt].target;
                avName[i].color = Muted;
            }
            if (sel)
            {
                // отметка выбранного
                var mark = TL(avHolders[i], 60, 5, 18, 18, "Sel");
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
