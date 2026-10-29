// Магазин оформления: фоны, стили интерфейса и рамки для аватара за деньги.
// • «Магазин» → вкладка «Оформление». Купленное можно надеть; «Сбросить оформление» возвращает обычный вид.
// • Перед покупкой — вопрос для взрослых (детская игра). Покупать можно только с аккаунтом:
//   покупки хранятся на сервере и не пропадут при переустановке или на другом телефоне.
// • Сейчас сервер работает в тестовом режиме: покупка проходит без списания денег. Когда подключите
//   платёжный сервис, сервер вернёт ссылку на оплату (payUrl) — игра её откроет; код игры менять не нужно.
// • Рамку аватара видят друзья (она отправляется на сервер вместе со статусом).
// Список товаров дублируется на сервере (SHOP_ITEMS в hatchmerge_server.py) — id и цены должны совпадать.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[Serializable] class NetShopReq { public string id, token, item; }
[Serializable] class NetShopItem { public string id; public int price; }
[Serializable]
class NetShopState
{
    public bool ok, account, granted;
    public string mode, error, payUrl;
    public string[] owned;
    public NetShopItem[] items;
}

public partial class MergeGame
{
    enum CosKind { Background, Style, Frame }

    struct CosItem
    {
        public string id, title;
        public CosKind kind;
        public int price;
        public Color a, b;            // фон: верх и низ; стиль: акцентный цвет; рамка: основной цвет
    }

    static CosItem Cos(string id, string title, CosKind k, int price, Color a, Color b) =>
        new CosItem { id = id, title = title, kind = k, price = price, a = a, b = b };

    static readonly CosItem[] CosItems =
    {
        Cos("bg_stars", "Звёздное небо", CosKind.Background, 79, new Color32(208, 200, 248, 255), new Color32(250, 238, 255, 255)),
        Cos("bg_meadow", "Мятный луг", CosKind.Background, 49, new Color32(212, 244, 228, 255), new Color32(244, 252, 236, 255)),
        Cos("ui_ocean", "Океан", CosKind.Style, 49, new Color32(40, 130, 200, 255), Color.white),
        Cos("ui_berry", "Ягодный", CosKind.Style, 49, new Color32(196, 56, 128, 255), Color.white),
        Cos("fr_gold", "Золотая", CosKind.Frame, 29, new Color32(255, 196, 70, 255), new Color32(255, 236, 160, 255)),
        Cos("fr_rainbow", "Радужная", CosKind.Frame, 49, Color.white, Color.white),
    };

    static readonly Color DefaultBg = new Color32(255, 243, 224, 255);
    static readonly Color DefaultAccent = new Color32(201, 60, 32, 255);
    static readonly string[] KindName = { "Фон", "Стиль интерфейса", "Рамка аватара" };

    readonly HashSet<string> cosOwned = new HashSet<string>();
    string cosBg = "", cosUi = "", cosFr = "";
    string shopMode = "test";
    bool shopLoaded, shopBusy, cosMode;
    readonly List<Graphic> accentGraphics = new List<Graphic>();

    RectTransform gemGroup, cosGroup, bgDecor;
    Image bgImage;
    readonly Image[] shopModeImg = new Image[2];
    readonly Text[] shopModeTxt = new Text[2];
    readonly Image[] cosBtnImg = new Image[6];
    readonly Text[] cosBtnTxt = new Text[6];
    Text cosNote;

    // вопрос для взрослых
    RectTransform gateOverlay;
    Text gateQuestion, gateWhat;
    readonly Text[] gateAnsTxt = new Text[3];
    int gateAnswer;
    string gateItem;

    // ============================================================
    //  Запуск
    // ============================================================
    void InitCosmetics()
    {
        foreach (var s in PlayerPrefs.GetString("shop_owned", "").Split(','))
            if (s.Length > 0) cosOwned.Add(s);
        cosBg = PlayerPrefs.GetString("cos_bg", "");
        cosUi = PlayerPrefs.GetString("cos_ui", "");
        cosFr = PlayerPrefs.GetString("cos_fr", "");

        // всё, что нарисовано акцентным цветом, запоминаем — стиль интерфейса перекрашивает это
        foreach (var g in canvasRt.GetComponentsInChildren<Graphic>(true))
            if (NearColor(g.color, DefaultAccent)) accentGraphics.Add(g);

        ApplyCosmetics();
    }

    static bool NearColor(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.03f && Mathf.Abs(a.a - b.a) < 0.05f;

    static int CosIndex(string id) => Array.FindIndex(CosItems, c => c.id == id);

    // ============================================================
    //  Применение оформления
    // ============================================================
    void ApplyCosmetics()
    {
        // стиль интерфейса — акцентный цвет кнопок и ссылок
        int ui = CosIndex(cosUi);
        Color accent = ui >= 0 ? CosItems[ui].a : DefaultAccent;
        if (!NearColor(accent, Red))
        {
            Color old = Red;
            Red = accent;
            foreach (var g in accentGraphics) if (g != null && NearColor(g.color, old)) g.color = accent;
            RefreshTop();
        }

        // фон
        if (bgImage == null)
        {
            var bgRt = (RectTransform)canvasRt.Find("BG");
            bgImage = bgRt.GetComponent<Image>();
            bgDecor = NewRt("Decor", bgRt);
            bgDecor.anchorMin = Vector2.zero;
            bgDecor.anchorMax = Vector2.one;
            bgDecor.offsetMin = bgDecor.offsetMax = Vector2.zero;
        }
        foreach (Transform ch in bgDecor) Destroy(ch.gameObject);
        int bg = CosIndex(cosBg);
        if (bg >= 0)
        {
            bgImage.sprite = GradientSprite(CosItems[bg].a, CosItems[bg].b);
            bgImage.color = Color.white;
            DrawBgDecor(cosBg);
        }
        else
        {
            bgImage.sprite = null;
            bgImage.color = DefaultBg;
        }
        if (Camera.main != null) Camera.main.backgroundColor = bg >= 0 ? CosItems[bg].a : DefaultBg;

        RefreshMyCard();
        if (tab == TabStart) RefreshStartMenu();
        RefreshCosShop();
    }

    static Sprite GradientSprite(Color top, Color bottom)
    {
        var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
    }

    void DrawBgDecor(string id)
    {
        var rnd = new System.Random(id == "bg_stars" ? 7 : 11);
        for (int i = 0; i < 26; i++)
        {
            var d = NewRt("Dot", bgDecor);
            float x = (float)rnd.NextDouble(), y = (float)rnd.NextDouble();
            d.anchorMin = d.anchorMax = new Vector2(x, y);
            float s = 8f + (float)rnd.NextDouble() * 14f;
            d.sizeDelta = new Vector2(s, s);
            if (id == "bg_stars")
            {
                d.localRotation = Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 90f);
                AddImg(d, starSpr != null ? starSpr : circleSpr, new Color(1f, 1f, 1f, 0.75f));
            }
            else
            {
                d.anchorMin = d.anchorMax = new Vector2(x, y * 0.35f);    // цветочки — внизу экрана
                Color[] pal = { C(255, 170, 200), C(255, 220, 110), C(255, 255, 255), C(170, 220, 255) };
                AddImg(d, circleSpr, pal[i % pal.Length] * new Color(1, 1, 1, 0.8f));
            }
        }
    }

    // рамка вокруг аватара (рисуется под животным)
    static void DrawFrame(RectTransform holder, string frame, float size, Vector2 center)
    {
        int k = CosIndex(frame);
        if (k < 0 || CosItems[k].kind != CosKind.Frame) return;
        float d = size * 1.32f;
        if (frame == "fr_rainbow")
        {
            Color[] rb = { C(255, 110, 110), C(255, 180, 90), C(255, 225, 100), C(130, 215, 130), C(110, 180, 255), C(185, 140, 255) };
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f;
                var dot = CC(holder, 0, 0, size * 0.16f, size * 0.16f, "FrameDot");
                dot.anchorMin = dot.anchorMax = new Vector2(0, 1);
                dot.anchoredPosition = new Vector2(center.x + Mathf.Cos(a) * d * 0.5f, -center.y + Mathf.Sin(a) * d * 0.5f);
                AddImg(dot, circleSpr, rb[i % rb.Length]);
            }
        }
        else
        {
            var ring = CC(holder, 0, 0, d, d, "Frame");
            ring.anchorMin = ring.anchorMax = new Vector2(0, 1);
            ring.anchoredPosition = new Vector2(center.x, -center.y);
            AddImg(ring, circleSpr, CosItems[k].a);
            AddImg(CC(ring, 0, 0, d * 0.84f, d * 0.84f, "In"), circleSpr, CosItems[k].b);
            for (int i = 0; i < 3; i++)
            {
                float a = (55f + i * 35f) * Mathf.Deg2Rad;
                var st = CC(ring, Mathf.Cos(a) * d * 0.5f, Mathf.Sin(a) * d * 0.5f, size * 0.22f, size * 0.22f, "Star");
                AddImg(st, starSpr != null ? starSpr : circleSpr, Color.white);
            }
        }
    }

    // ============================================================
    //  Магазин: вкладка «Оформление»
    // ============================================================
    void BuildCosShop()
    {
        // переключатель «Бонусы / Оформление»
        string[] modes = { "Бонусы", "Оформление" };
        float[] w = { 80, 104 };
        for (int i = 0; i < 2; i++)
        {
            int m = i;
            var pill = TL(shopPanel, i == 0 ? 182 : 266, 84, w[i], 32, "ShopMode");
            shopModeImg[i] = Round(pill, Color.white, 16);
            shopModeTxt[i] = Label(pill, modes[i], 13, Ink, TextAnchor.MiddleCenter, 0, 0, w[i], 32);
            AddBtn(pill, () => SetCosMode(m == 1), 2);
        }

        // старое содержимое магазина — в группу «Бонусы»
        gemGroup = TL(shopPanel, 0, 0, 390, 700, "Gems");
        var move = new List<Transform>();
        foreach (Transform ch in shopPanel)
            if (ch != gemGroup && ch.name != "ShopMode" && !(ch.GetComponent<Text>() is Text t && t.text == "Магазин")) move.Add(ch);
        foreach (var ch in move) ch.SetParent(gemGroup, false);

        cosGroup = TL(shopPanel, 0, 0, 390, 700, "Cosmetics");
        cosNote = Label(cosGroup, "", 12, Muted, TextAnchor.MiddleLeft, 20, 118, 350, 20);
        for (int i = 0; i < CosItems.Length; i++)
        {
            int idx = i;
            var it = CosItems[i];
            var card = TL(cosGroup, 20, 146 + i * 70, 350, 62, "Cos");
            Round(card, Color.white, 20);
            var prev = TL(card, 10, 7, 48, 48, "Preview");
            DrawCosPreview(prev, it);
            Label(card, it.title, 15, Ink, TextAnchor.MiddleLeft, 70, 8, 170, 24);
            Label(card, KindName[(int)it.kind], 12, Muted, TextAnchor.MiddleLeft, 70, 32, 170, 20);
            var btn = TL(card, 240, 13, 98, 36, "Buy");
            cosBtnImg[i] = Round(btn, Red, 18);
            cosBtnTxt[i] = Label(btn, "", 14, Color.white, TextAnchor.MiddleCenter, 0, 0, 98, 36);
            AddBtn(btn, () => CosButton(idx), 2);
        }
        var reset = TL(cosGroup, 95, 572, 200, 30, "Reset");
        Label(reset, "Сбросить оформление", 14, Red, TextAnchor.MiddleCenter, 0, 0, 200, 30);
        AddBtn(reset, () =>
        {
            GameAudio.Click();
            SetCosmetic(CosKind.Background, ""); SetCosmetic(CosKind.Style, ""); SetCosmetic(CosKind.Frame, "");
            Toast("Обычное оформление");
        }, 2);
        Label(cosGroup, "Покупки хранятся в аккаунте — они не пропадут на другом телефоне", 11, Muted,
            TextAnchor.MiddleCenter, 20, 604, 350, 20);

        BuildParentGate();
        ApplyCosMode();
    }

    void DrawCosPreview(RectTransform prev, CosItem it)
    {
        switch (it.kind)
        {
            case CosKind.Background:
                var tImg = prev.gameObject.AddComponent<Image>();
                tImg.sprite = GradientSprite(it.a, it.b);
                tImg.raycastTarget = false;
                AddImg(CC(prev, it.id == "bg_stars" ? 8 : -8, it.id == "bg_stars" ? 10 : -12, 12, 12, "Dot"),
                    it.id == "bg_stars" && starSpr != null ? starSpr : circleSpr, it.id == "bg_stars" ? Color.white : C(255, 170, 200));
                break;
            case CosKind.Style:
                Round(prev, C(246, 240, 250), 14);
                Round(CC(prev, 0, 0, 34, 20, "Btn"), it.a, 10);
                break;
            default:
                Round(prev, C(246, 240, 250), 14);
                DrawFrame(prev, it.id, 26f, new Vector2(24, 24));
                Egg.Make(prev, new Vector2(24, 24), 1, 0.42f, true);
                break;
        }
    }

    void SetCosMode(bool on)
    {
        if (cosMode == on) return;
        cosMode = on;
        GameAudio.Click();
        ApplyCosMode();
        if (on) StartCoroutine(LoadShopState());
    }

    void ApplyCosMode()
    {
        if (gemGroup == null) return;
        gemGroup.gameObject.SetActive(!cosMode);
        cosGroup.gameObject.SetActive(cosMode);
        for (int i = 0; i < 2; i++)
        {
            bool sel = (i == 1) == cosMode;
            shopModeImg[i].color = sel ? Ink : Color.white;
            shopModeTxt[i].color = sel ? Color.white : Ink;
        }
        RefreshCosShop();
    }

    string Equipped(CosKind k) => k == CosKind.Background ? cosBg : k == CosKind.Style ? cosUi : cosFr;

    void RefreshCosShop()
    {
        if (cosGroup == null) return;
        cosNote.text = shopMode == "test" ? "Тестовый режим — деньги не списываются" : "Покупка за рубли";
        for (int i = 0; i < CosItems.Length; i++)
        {
            var it = CosItems[i];
            bool owned = cosOwned.Contains(it.id), on = Equipped(it.kind) == it.id;
            cosBtnImg[i].color = on ? Ink : owned ? C(238, 227, 247) : Red;
            cosBtnTxt[i].color = on || !owned ? Color.white : Ink;
            cosBtnTxt[i].text = on ? "Выбрано" : owned ? "Выбрать" : it.price + " ₽";
        }
    }

    void CosButton(int i)
    {
        var it = CosItems[i];
        if (cosOwned.Contains(it.id))
        {
            bool on = Equipped(it.kind) == it.id;
            SetCosmetic(it.kind, on ? "" : it.id);              // повторное нажатие снимает
            GameAudio.Click();
            return;
        }
        if (shopBusy) return;
        if (!HasAccount)
        {
            GameAudio.Deny();
            Toast("Покупки — только с аккаунтом");
            SetTab(TabAccount);
            return;
        }
        if (netState != NetState.Online) { GameAudio.Deny(); Toast("Нет связи с сервером"); KickNet(); return; }
        OpenParentGate(it);
    }

    void SetCosmetic(CosKind k, string id)
    {
        if (k == CosKind.Background) { cosBg = id; PlayerPrefs.SetString("cos_bg", id); }
        else if (k == CosKind.Style) { cosUi = id; PlayerPrefs.SetString("cos_ui", id); }
        else { cosFr = id; PlayerPrefs.SetString("cos_fr", id); KickNet(); }      // рамку сразу видят друзья
        ApplyCosmetics();
        Save();
    }

    // ============================================================
    //  Сервер
    // ============================================================
    IEnumerator LoadShopState()
    {
        if (netState != NetState.Online || string.IsNullOrEmpty(netId)) yield break;
        NetShopState r = null;
        yield return Http("POST", netUrl, "/api/shop/state", JsonUtility.ToJson(new NetAuthReq { id = netId, token = netToken }),
            (c, t) => { if (c == 200) r = Parse<NetShopState>(t); });
        if (r == null || !r.ok) yield break;
        shopMode = string.IsNullOrEmpty(r.mode) ? "test" : r.mode;
        SetOwned(r.owned);
        shopLoaded = true;
    }

    void SetOwned(string[] owned)
    {
        cosOwned.Clear();
        if (owned != null) foreach (var s in owned) if (!string.IsNullOrEmpty(s)) cosOwned.Add(s);
        PlayerPrefs.SetString("shop_owned", string.Join(",", cosOwned));
        // надетое, но больше не купленное (например, покупку отозвали) — снять
        bool changed = false;
        if (cosBg != "" && !cosOwned.Contains(cosBg)) { cosBg = ""; PlayerPrefs.SetString("cos_bg", ""); changed = true; }
        if (cosUi != "" && !cosOwned.Contains(cosUi)) { cosUi = ""; PlayerPrefs.SetString("cos_ui", ""); changed = true; }
        if (cosFr != "" && !cosOwned.Contains(cosFr)) { cosFr = ""; PlayerPrefs.SetString("cos_fr", ""); changed = true; }
        if (changed) ApplyCosmetics(); else RefreshCosShop();
    }

    IEnumerator DoBuy(string item)
    {
        shopBusy = true;
        Toast("Оформляем покупку…");
        long code = 0;
        NetShopState r = null;
        yield return Http("POST", netUrl, "/api/shop/buy", JsonUtility.ToJson(new NetShopReq { id = netId, token = netToken, item = item }),
            (c, t) => { code = c; r = Parse<NetShopState>(t); }, 20);
        shopBusy = false;

        if (code == 200 && r != null && r.ok && r.granted)
        {
            SetOwned(r.owned);
            int k = CosIndex(item);
            if (k >= 0) SetCosmetic(CosItems[k].kind, item);         // сразу надеть
            GameAudio.Hatch();
            Toast("Куплено!");
        }
        else if (code == 200 && r != null && !string.IsNullOrEmpty(r.payUrl))
        {
            Application.OpenURL(r.payUrl);                          // оплата в браузере (когда подключат платёжный сервис)
            Toast("Завершите оплату в браузере");
        }
        else
        {
            GameAudio.Deny();
            string err = r != null ? r.error : null;
            Toast(err == "need_account" ? "Покупки — только с аккаунтом"
                : err == "payments_off" ? "Покупки пока недоступны"
                : code == 0 ? "Нет связи с сервером" : "Не получилось, попробуйте ещё раз");
        }
    }

    // ============================================================
    //  Вопрос для взрослых
    // ============================================================
    void BuildParentGate()
    {
        gateOverlay = NewRt("ParentGate", canvasRt);
        gateOverlay.anchorMin = Vector2.zero;
        gateOverlay.anchorMax = Vector2.one;
        gateOverlay.offsetMin = gateOverlay.offsetMax = Vector2.zero;
        AddImg(gateOverlay, null, new Color(0.23f, 0.16f, 0.29f, 0.55f));

        var box = CC(gateOverlay, 0, 10, 330, 380, "Box");
        Round(box, Color.white, 28);
        Label(box, "Вопрос для взрослых", 20, Ink, TextAnchor.MiddleCenter, 0, 22, 330, 30);
        gateWhat = WrapLabel(box, "", 14, Muted, TextAnchor.UpperCenter, 20, 56, 290, 44);
        gateQuestion = Label(box, "", 30, Ink, TextAnchor.MiddleCenter, 0, 104, 330, 48);

        var list = new List<Btn>();
        for (int i = 0; i < 3; i++)
        {
            int n = i;
            var b = TL(box, 20 + i * 100, 168, 90, 60, "Answer");
            Round(b, C(238, 227, 247), 22);
            gateAnsTxt[i] = Label(b, "", 22, Ink, TextAnchor.MiddleCenter, 0, 0, 90, 60);
            list.Add(new Btn { rt = b, act = () => GateAnswer(n), tab = -1 });
        }
        var cancel = TL(box, 20, 244, 290, 48, "Cancel");
        Label(cancel, "Отмена", 16, Muted, TextAnchor.MiddleCenter, 0, 0, 290, 48);
        list.Add(new Btn { rt = cancel, act = () => { GameAudio.Click(); gateOverlay.gameObject.SetActive(false); }, tab = -1 });
        WrapLabel(box, "Покупку должен подтвердить взрослый", 12, Muted, TextAnchor.UpperCenter, 20, 300, 290, 40);
        list.Add(new Btn { rt = gateOverlay, act = () => { }, tab = -1 });     // заглушка: под окном ничего не нажимается
        btns.InsertRange(0, list);
        gateOverlay.gameObject.SetActive(false);
    }

    void OpenParentGate(CosItem it)
    {
        gateItem = it.id;
        int a = UnityEngine.Random.Range(6, 10), b = UnityEngine.Random.Range(6, 10);
        gateAnswer = a * b;
        gateQuestion.text = a + " × " + b + " = ?";
        gateWhat.text = KindName[(int)it.kind] + " «" + it.title + "» за " + it.price + " ₽" + (shopMode == "test" ? " (тест, без оплаты)" : "");
        int correct = UnityEngine.Random.Range(0, 3);
        var used = new HashSet<int> { gateAnswer };
        for (int i = 0; i < 3; i++)
        {
            int v = gateAnswer;
            if (i != correct)
                do v = gateAnswer + UnityEngine.Random.Range(-12, 13); while (v <= 0 || used.Contains(v));
            used.Add(v);
            gateAnsTxt[i].text = v.ToString();
        }
        gateOverlay.SetAsLastSibling();
        gateOverlay.gameObject.SetActive(true);
        GameAudio.Click();
    }

    void GateAnswer(int i)
    {
        gateOverlay.gameObject.SetActive(false);
        if (gateAnsTxt[i].text != gateAnswer.ToString()) { GameAudio.Deny(); Toast("Неверный ответ"); return; }
        StartCoroutine(DoBuy(gateItem));
    }
}
