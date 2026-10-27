// Меню: вкладки, зоопарк, магазин, настройки, сохранение.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    // ============================================================
    //  Меню: вкладки, зоопарк, магазин, настройки
    // ============================================================
    struct Btn { public RectTransform rt; public System.Action act; public int tab; }
    readonly List<Btn> btns = new List<Btn>();
    int tab;                                   // 0 игра, 1 зоопарк, 2 магазин, 3 настройки, 4 стартовое меню, 5 игроки
    RectTransform nav, gameRoot, zooPanel, shopPanel, setPanel, zooGrid, menuFx;
    // кнопки нижней панели: какая вкладка открывается каждой из них
    static readonly int[] NavTabs = { 0, 1, 2, TabPlayers, TabStart };
    readonly RectTransform[] tabBtn = new RectTransform[5];
    readonly Image[] tabImg = new Image[5];
    readonly Text[] tabTxt = new Text[5];
    static readonly int[] Prices = { 4, 6, 12 };
    readonly Image[] priceImg = new Image[3];
    Image musicTrack, sfxTrack;
    RectTransform musicKnob, sfxKnob;
    Text resetTxt;
    float resetArm;

    void AddBtn(RectTransform rt, System.Action act, int tabIdx)
    {
        btns.Add(new Btn { rt = rt, act = act, tab = tabIdx });
    }

    void BuildMenu()
    {
        // нижняя панель вкладок (привязана к низу экрана)
        nav = NewRt("Nav", canvasRt);
        nav.anchorMin = nav.anchorMax = new Vector2(0.5f, 0f);
        nav.pivot = new Vector2(0.5f, 0f);
        nav.sizeDelta = new Vector2(350, 60);
        Round(nav, Color.white, 30);
        string[] names = { "Игра", "Зоопарк", "Магазин", "Игроки", "Меню" };
        for (int i = 0; i < NavTabs.Length; i++)
        {
            int idx = NavTabs[i];
            var b = TL(nav, 6f + i * 67.6f, 6, 65.6f, 48, "Tab");
            tabImg[i] = Round(b, Color.white, 24);
            tabTxt[i] = Label(b, names[i], 12, Ink, TextAnchor.MiddleCenter, 0, 0, 65.6f, 48);
            tabBtn[i] = b;
            AddBtn(b, () => SetTab(idx), -1);
        }

        // --- зоопарк ---
        zooPanel = TL(content, 0, 0, 390, 700, "ZooPanel");
        Label(zooPanel, "Мои животные", 22, Ink, TextAnchor.MiddleLeft, 20, 82, 260, 36);
        zooGrid = TL(zooPanel, 0, 0, 390, 700, "Grid");

        // --- магазин ---
        shopPanel = TL(content, 0, 0, 390, 700, "ShopPanel");
        Label(shopPanel, "Магазин", 22, Ink, TextAnchor.MiddleLeft, 20, 82, 260, 36);
        Label(shopPanel, "Кристаллы дают за каждое новое животное", 13, Muted, TextAnchor.MiddleLeft, 20, 118, 350, 20);
        string[] st = { "Полная энергия", "Три яйца 2 уровня", "Яйцо 4 уровня" };
        string[] ss = { "Восстановить до 10 / 10", "Появятся на свободных клетках", "Сразу окажется на поле" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var card = TL(shopPanel, 20, 150 + i * 96, 350, 84, "Item");
            Round(card, Color.white, 22);
            Label(card, st[i], 16, Ink, TextAnchor.MiddleLeft, 16, 14, 230, 28);
            Label(card, ss[i], 13, Muted, TextAnchor.MiddleLeft, 16, 42, 230, 24);
            var pill = TL(card, 258, 24, 76, 36, "Price");
            priceImg[i] = Round(pill, Red, 18);
            GemIcon(pill, -22, 0, 18);
            Label(pill, Prices[i].ToString(), 16, Color.white, TextAnchor.MiddleLeft, 40, 0, 32, 36);
            AddBtn(pill, () => Buy(idx), 2);
        }

        // --- настройки ---
        setPanel = TL(content, 0, 0, 390, 700, "SettingsPanel");
        Label(setPanel, "Настройки", 22, Ink, TextAnchor.MiddleLeft, 20, 82, 260, 36);
        MakeSwitch(setPanel, "Музыка", 134, out musicTrack, out musicKnob, ToggleMusic);
        MakeSwitch(setPanel, "Звуки", 206, out sfxTrack, out sfxKnob, ToggleSfx);
        var rc = TL(setPanel, 20, 278, 350, 64, "Reset");
        Round(rc, Color.white, 22);
        resetTxt = Label(rc, "Сбросить прогресс", 16, Red, TextAnchor.MiddleCenter, 0, 0, 350, 64);
        AddBtn(rc, ArmReset, 3);
        Label(setPanel, "Очки и животные сохраняются автоматически", 12, Muted, TextAnchor.MiddleCenter, 20, 352, 350, 20);
        BuildServerRow(setPanel, 392);

        BuildStartMenu();
        BuildPlayers();

        menuFx = TL(content, 0, 0, 390, 700, "MenuFX");
    }

    void MakeSwitch(RectTransform parent, string title, float y, out Image track, out RectTransform knob, System.Action toggle)
    {
        var card = TL(parent, 20, y, 350, 60, "Row");
        Round(card, Color.white, 22);
        Label(card, title, 16, Ink, TextAnchor.MiddleLeft, 16, 0, 200, 60);
        var tr = TL(card, 278, 15, 56, 30, "Track");
        track = Round(tr, Color.white, 15);
        knob = TL(card, 281, 18, 24, 24, "Knob");
        AddImg(knob, circleSpr, Color.white);
        AddBtn(tr, toggle, 3);
    }

    void ApplyToggles()
    {
        Color on = C(110, 200, 160), off = C(217, 205, 230);
        musicTrack.color = GameAudio.MusicOn ? on : off;
        musicKnob.anchoredPosition = new Vector2(GameAudio.MusicOn ? 305f : 281f, -18f);
        sfxTrack.color = GameAudio.SfxOn ? on : off;
        sfxKnob.anchoredPosition = new Vector2(GameAudio.SfxOn ? 305f : 281f, -18f);
    }

    void ToggleMusic() { GameAudio.SetMusic(!GameAudio.MusicOn); ApplyToggles(); Save(); GameAudio.Click(); }
    void ToggleSfx() { GameAudio.SetSfx(!GameAudio.SfxOn); ApplyToggles(); Save(); GameAudio.Click(); }

    void SetTab(int i)
    {
        if (i == tab) return;
        if (dragged != null)
        {
            dragged.dragging = false;
            dragged.transform.SetParent(eggLayer, false);
            dragged.home = CellCenter(dragFrom.x, dragFrom.y);
            dragged = null;
        }
        btnPressed = false;
        btnRt.localScale = Vector3.one;
        tab = i;
        GameAudio.Click();
        ApplyTab();
    }

    void ApplyTab()
    {
        gameRoot.gameObject.SetActive(tab == 0);
        zooPanel.gameObject.SetActive(tab == 1);
        shopPanel.gameObject.SetActive(tab == 2);
        setPanel.gameObject.SetActive(tab == 3);
        startPanel.gameObject.SetActive(tab == TabStart);
        playersPanel.gameObject.SetActive(tab == TabPlayers);

        // в стартовом меню прячем нижнюю панель и счётчики сверху
        bool inMenu = tab == TabStart;
        nav.gameObject.SetActive(!inMenu);
        levelTxt.gameObject.SetActive(!inMenu);
        coinTxt.transform.parent.gameObject.SetActive(!inMenu);
        gemTxt.transform.parent.gameObject.SetActive(!inMenu);

        if (tab == 1) RefreshZooPanel();
        if (tab == TabStart) RefreshStartMenu();
        if (tab == TabPlayers) OnPlayersOpened();
        for (int k = 0; k < NavTabs.Length; k++)
        {
            bool sel = NavTabs[k] == tab;
            tabImg[k].color = sel ? Ink : Color.white;
            tabTxt[k].color = sel ? Color.white : Ink;
        }
    }

    void RefreshZooPanel()
    {
        foreach (Transform ch in zooGrid) Destroy(ch.gameObject);
        for (int i = 0; i < Zoo.Length; i++)
        {
            var card = TL(zooGrid, 20 + (i % 2) * 185, 128 + (i / 2) * 124, 165, 114, "Card");
            Round(card, Color.white, 20);
            if (i < zooCount)
            {
                var cr = MakeCreature(card, i, 52f);
                cr.anchoredPosition = new Vector2(82.5f, -42f);
                Label(card, Zoo[i].name, 13, Ink, TextAnchor.MiddleCenter, 0, 80, 165, 26);
            }
            else
            {
                var q = TL(card, 54.5f, 16, 56, 56, "Locked");
                Round(q, C(238, 227, 247), 28);
                Label(q, "?", 22, Ink, TextAnchor.MiddleCenter, 0, 0, 56, 56);
                Label(card, "Яйцо уровня " + Zoo[i].target, 13, Muted, TextAnchor.MiddleCenter, 0, 80, 165, 26);
            }
        }
    }

    // ---------- Магазин ----------
    List<Vector2Int> FreeCells()
    {
        var l = new List<Vector2Int>();
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                if (grid[c, r] == null) l.Add(new Vector2Int(c, r));
        return l;
    }

    void Toast(string s) { FloatText(s, new Vector2(195, 470), 17, 320, menuFx); }

    void Buy(int i)
    {
        if (gems < Prices[i]) { GameAudio.Deny(); Toast("Не хватает кристаллов"); return; }

        if (i == 0)
        {
            if (energy >= MaxEnergy) { GameAudio.Deny(); Toast("Энергия уже полная"); return; }
            energy = MaxEnergy;
        }
        else
        {
            int n = i == 1 ? 3 : 1, lvl = i == 1 ? 2 : 4;
            var free = FreeCells();
            if (free.Count < n) { GameAudio.Deny(); Toast("Мало места на поле"); return; }
            for (int k = 0; k < n; k++)
            {
                int j = Random.Range(0, free.Count);
                Spawn(free[j].x, free[j].y, lvl);
                free.RemoveAt(j);
            }
        }

        gems -= Prices[i];
        GameAudio.Buy();
        Toast("Куплено!");
        AfterMove();
    }

    // ---------- Настройки ----------
    void ArmReset()
    {
        if (resetArm > 0f)
        {
            resetArm = 0f;
            resetTxt.text = "Сбросить прогресс";
            ResetGame();
            return;
        }
        resetArm = 3f;
        resetTxt.text = "Нажмите ещё раз для сброса";
        GameAudio.Click();
    }

    void ResetGame()
    {
        score = 0; gems = 0; zooCount = 0; energy = MaxEnergy; combo = 0;
        ClearHint();
        foreach (var e in grid) if (e != null) Destroy(e.gameObject);
        System.Array.Clear(grid, 0, grid.Length);
        Seed();
        RefreshAll();
        Save();
        KickNet();
        SetTab(0);
        Toast("Прогресс сброшен");
    }

    // ---------- Сохранение ----------
    void Save()
    {
        PlayerPrefs.SetInt("eg_score", score);
        PlayerPrefs.SetInt("eg_gems", gems);
        PlayerPrefs.SetInt("eg_zoo", zooCount);
        PlayerPrefs.SetInt("eg_music", GameAudio.MusicOn ? 1 : 0);
        PlayerPrefs.SetInt("eg_sfx", GameAudio.SfxOn ? 1 : 0);
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) { Save(); PlayerPrefs.Save(); NetGoodbye(); }
        else KickNet();
    }

    void OnApplicationQuit() { Save(); PlayerPrefs.Save(); NetGoodbye(); }
}
