// Интерфейс игрового экрана: помощники разметки, построение экрана, обновление счётчиков.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    // ============================================================
    //  UI-помощники (координаты «дизайн»: 390 х 700, начало слева сверху, y вниз)
    // ============================================================
    internal static RectTransform NewRt(string n, Transform parent)
    {
        var g = new GameObject(n, typeof(RectTransform));
        var rt = (RectTransform)g.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    internal static RectTransform TL(Transform p, float x, float y, float w, float h, string n = "Box")
    {
        var rt = NewRt(n, p);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    internal static RectTransform CC(Transform p, float x, float y, float w, float h, string n = "Part")
    {
        var rt = NewRt(n, p);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    internal static Image AddImg(RectTransform rt, Sprite s, Color c)
    {
        var im = rt.gameObject.AddComponent<Image>();
        im.sprite = s; im.color = c; im.raycastTarget = false;
        return im;
    }

    internal static Image Round(RectTransform rt, Color c, float radius)
    {
        var im = AddImg(rt, roundSpr, c);
        im.type = Image.Type.Sliced;
        im.pixelsPerUnitMultiplier = 24f / radius;
        return im;
    }

    internal static Text AddText(RectTransform rt, string s, int size, Color c, TextAnchor a)
    {
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = fontStyle;
        t.color = c; t.alignment = a; t.text = s; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static Text Label(Transform p, string s, int size, Color c, TextAnchor a, float x, float y, float w, float h)
        => AddText(TL(p, x, y, w, h, "Label"), s, size, c, a);

    internal static float Back(float t) { t -= 1f; return 1f + 2.70158f * t * t * t + 1.70158f * t * t; }

    // ============================================================
    //  Построение интерфейса по макету
    // ============================================================
    void BuildUI()
    {
        var cg = new GameObject("Canvas");
        var canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(390, 844);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0f;
        canvasRt = (RectTransform)cg.transform;

        var bg = NewRt("BG", cg.transform);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        AddImg(bg, null, C(255, 243, 224));

        content = NewRt("Content", cg.transform);
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(390, 700);

        // --- верхняя панель ---
        levelTxt = Label(content, "", 22, Ink, TextAnchor.MiddleLeft, 20, 22, 170, 44);

        var gemPill = TL(content, 302, 29, 68, 34, "GemPill");
        Round(gemPill, Color.white, 17);
        GemIcon(gemPill, -20, 0, 22);
        gemTxt = Label(gemPill, "0", 15, Ink, TextAnchor.MiddleLeft, 34, 0, 32, 34);

        var coinPill = TL(content, 206, 29, 88, 34, "CoinPill");
        Round(coinPill, Color.white, 17);
        CoinIcon(coinPill, -30, 0, 28);
        coinTxt = Label(coinPill, "0", 15, Ink, TextAnchor.MiddleLeft, 38, 0, 48, 34);

        int gStart = content.childCount;

        // --- карточка задания ---
        var qc = TL(content, 20, 82, 350, 76, "Quest");
        Round(qc, Color.white, 24);
        questEgg = Egg.Make(qc, new Vector2(37, 38), 5, 0.9f, true);
        questTitle = Label(qc, "", 16, Ink, TextAnchor.MiddleLeft, 72, 16, 168, 28);
        // длинные имена животных уменьшаются, чтобы не наезжать на счётчик справа
        questTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        questTitle.verticalOverflow = VerticalWrapMode.Truncate;
        questTitle.resizeTextForBestFit = true;
        questTitle.resizeTextMinSize = 10;
        questTitle.resizeTextMaxSize = 16;
        Round(TL(qc, 72, 49, 223, 10, "Track"), C(238, 227, 247), 5);
        questFill = TL(qc, 72, 49, 10, 10, "Fill");
        Round(questFill, C(154, 111, 224), 5);
        questProg = Label(qc, "", 15, Ink, TextAnchor.MiddleRight, 244, 16, 90, 28);
        questReward = Label(qc, "", 13, Muted, TextAnchor.MiddleRight, 244, 34, 90, 20);

        // --- поле ---
        Round(TL(content, 20, 172, 350, 344, "Board"), C(255, 224, 184), 28);
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                var p = CellCenter(c, r);
                var slot = TL(content, p.x - 28, p.y - 28, 56, 56, "Slot");
                Round(slot, C(240, 211, 170), 16);
                Round(TL(slot, 0, 3, 56, 53, "In"), C(255, 239, 214), 16);
            }

        // --- зоопарк ---
        var zc = TL(content, 20, 530, 350, 60, "ZooCard");
        Round(zc, Color.white, 24);
        Label(zc, "Мой зоопарк", 15, Ink, TextAnchor.MiddleLeft, 16, 9, 150, 24);
        zooTxt = Label(zc, "", 13, Muted, TextAnchor.MiddleLeft, 16, 27, 150, 20);
        zooIcons = TL(zc, 0, 0, 350, 60, "Icons");
        qmRt = TL(zc, 294, 10, 40, 40, "Next");
        Round(qmRt, C(238, 227, 247), 20);
        Label(qmRt, "?", 18, Ink, TextAnchor.MiddleCenter, 0, 0, 40, 40);

        // --- кнопка и энергия ---
        btnRt = TL(content, 20, 604, 206, 56, "NewEgg");
        btnImg = Round(btnRt, Red, 28);
        Label(btnRt, "+ Новое яйцо", 20, Color.white, TextAnchor.MiddleCenter, 0, 0, 206, 56);
        btnRt.pivot = new Vector2(0.5f, 0.5f);
        btnRt.anchoredPosition += new Vector2(103f, -28f);

        var ep = TL(content, 238, 604, 132, 56, "Energy");
        Round(ep, Color.white, 28);
        energyTxt = Label(ep, "", 15, Ink, TextAnchor.MiddleCenter, 0, 0, 132, 56);

        // --- слои: яйца, перетаскиваемое яйцо, эффекты ---
        eggLayer = TL(content, 0, 0, 390, 700, "Eggs");
        dragLayer = TL(content, 0, 0, 390, 700, "Drag");
        fxLayer = TL(content, 0, 0, 390, 700, "FX");

        // всё, что относится к игре (кроме верхней панели), переезжает в gameRoot
        gameRoot = TL(content, 0, 0, 390, 700, "Game");
        var kids = new List<Transform>();
        for (int i = gStart; i < content.childCount - 1; i++) kids.Add(content.GetChild(i));
        foreach (var k in kids) k.SetParent(gameRoot, false);
    }

    static Vector2 CellCenter(int c, int r) => new Vector2(GridX0 + 28f + c * Pitch, GridY0 + 28f + r * Pitch);

    static bool ToCell(Vector2 d, out Vector2Int cell)
    {
        int c = Mathf.FloorToInt((d.x - (GridX0 - 4f)) / Pitch);
        int r = Mathf.FloorToInt((d.y - (GridY0 - 4f)) / Pitch);
        cell = new Vector2Int(c, r);
        return c >= 0 && c < Cols && r >= 0 && r < Rows;
    }

    // ============================================================
    //  Обновление экрана
    // ============================================================
    static string Fmt(int n)
    {
        if (n >= 1000000) return (n / 1000000f).ToString("0.#") + "M";
        if (n >= 100000) return (n / 1000) + "k";
        string s = n.ToString();
        if (n >= 1000) s = s.Insert(s.Length - 3, " ");
        return s;
    }

    int BestLevel()
    {
        int b = 0;
        foreach (var e in grid) if (e != null && e.level > b) b = e.level;
        return b;
    }

    bool HasEmpty()
    {
        foreach (var e in grid) if (e == null) return true;
        return false;
    }

    void RefreshAll() { RefreshTop(); RefreshQuest(); RefreshZoo(); }

    void RefreshTop()
    {
        levelTxt.text = "Уровень " + (zooCount + 1);
        coinTxt.text = Fmt(score);
        gemTxt.text = gems.ToString();
        energyTxt.text = "Энергия " + energy + "/" + MaxEnergy;
        btnImg.color = (energy > 0 && HasEmpty()) ? Red : C(216, 201, 190);
        for (int i = 0; i < priceImg.Length; i++)
            if (priceImg[i] != null) priceImg[i].color = gems >= Prices[i] ? Red : C(216, 201, 190);
        RefreshCosShop();                    // цены оформления зависят от монет
    }

    void RefreshQuest()
    {
        if (zooCount >= Zoo.Length)
        {
            questTitle.text = "Все животные открыты!";
            questProg.text = ""; questReward.text = "";
            questFill.sizeDelta = new Vector2(223, 10);
            questEgg.gameObject.SetActive(false);
            return;
        }
        var z = Zoo[zooCount];
        int best = Mathf.Min(BestLevel(), z.target);
        questTitle.text = "Вылупи: " + z.name;
        questProg.text = best + " / " + z.target;
        questReward.text = "+50";
        questFill.sizeDelta = new Vector2(Mathf.Max(10f, 223f * best / z.target), 10f);
        questEgg.gameObject.SetActive(true);
        questEgg.Setup(z.target);
    }

    void RefreshZoo()
    {
        foreach (Transform ch in zooIcons) Destroy(ch.gameObject);
        zooTxt.text = zooCount + " / " + Zoo.Length;
        for (int s = 0; s < 3; s++)
        {
            int idx = zooCount - 3 + s;
            float cx = 170f + 48f * s;
            if (idx >= 0)
            {
                var cr = MakeCreature(zooIcons, idx, 32f);
                cr.anchoredPosition = new Vector2(cx, -30f);
            }
            else AddImg(TL(zooIcons, cx - 16f, 14f, 32f, 32f, "Empty"), circleSpr, C(243, 234, 249));
        }
        qmRt.gameObject.SetActive(zooCount < Zoo.Length);
    }
}
