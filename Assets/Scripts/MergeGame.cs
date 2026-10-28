// Яйца-сюрпризы — основной класс игры: состояние, запуск, игровой цикл и ввод.
// Остальные части этого же класса (partial): MergeGame.UI.cs, MergeGame.Menu.cs,
// MergeGame.Visuals.cs, MergeGame.Art.cs.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public partial class MergeGame : MonoBehaviour
{
    const int Cols = 5, Rows = 5, MaxLevel = 7, MaxEnergy = 10;
    const float Pitch = 64f, GridX0 = 39f, GridY0 = 188f, EnergyRegen = 10f;
    static readonly Rect BtnRect = new Rect(20, 604, 206, 56);

    // ---------- Цвета ----------
    static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);
    static readonly Color Ink = new Color(59 / 255f, 42 / 255f, 74 / 255f);
    static readonly Color Muted = new Color(90 / 255f, 72 / 255f, 104 / 255f);
    static readonly Color Red = new Color(201 / 255f, 60 / 255f, 32 / 255f);

    internal static readonly Color[] Palette =
    {
        Color.clear,
        new Color32(246, 214, 168, 255), new Color32(158, 216, 198, 255),
        new Color32(142, 197, 242, 255), new Color32(245, 167, 200, 255),
        new Color32(199, 166, 242, 255), new Color32(255, 201, 120, 255),
        new Color32(255, 140, 120, 255),
    };

    // ---------- Животные ----------
    struct Creature { public string name; public Color color; public int look; public int target; }

    static Creature Cr(string n, int r, int g, int b, int look, int target) =>
        new Creature { name = n, color = C(r, g, b), look = look, target = target };

    static readonly Creature[] Zoo =
    {
        Cr("Облачко-пушистик", 142, 197, 242, 0, 4),
        Cr("Дракончик-тапок", 199, 166, 242, 1, 5),
        Cr("Медузка-шарик", 245, 167, 200, 2, 5),
        Cr("Лисёнок-носок", 255, 168, 100, 3, 6),
        Cr("Котик-клубочек", 158, 216, 198, 4, 6),
        Cr("Совёнок-пончик", 255, 209, 102, 5, 7),
        Cr("Зайка-варежка", 250, 200, 215, 6, 7),
        Cr("Единорожек-пирожок", 190, 160, 240, 7, 7),
    };

    // ---------- Состояние ----------
    internal static Font font;
    internal static Sprite eggSpr, circleSpr, roundSpr, ringSpr;
    internal static FontStyle fontStyle = FontStyle.Bold;
    static MergeGame inst;

    readonly Egg[,] grid = new Egg[Cols, Rows];
    RectTransform canvasRt, content, eggLayer, dragLayer, fxLayer, questFill, btnRt, zooIcons, qmRt;
    Text levelTxt, coinTxt, gemTxt, energyTxt, questTitle, questProg, questReward, zooTxt;
    Image btnImg;
    Egg questEgg, dragged, hintA, hintB;
    Vector2Int dragFrom;
    Vector2 grab;
    int score, gems, zooCount, energy = MaxEnergy, combo;
    float energyT, lastMerge = -10f, idle;
    bool btnPressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (inst == null) new GameObject("MergeGame").AddComponent<MergeGame>(); }

    void Awake() { inst = this; }

    void Start()
    {
        Application.targetFrameRate = 60;
        LoadAssets();
        score = PlayerPrefs.GetInt("eg_score", 0);
        gems = PlayerPrefs.GetInt("eg_gems", 0);
        zooCount = Mathf.Clamp(PlayerPrefs.GetInt("eg_zoo", 0), 0, Zoo.Length);
        energy = Mathf.Clamp(PlayerPrefs.GetInt("eg_energy", MaxEnergy), 0, MaxGrantedEnergy);
        bool mOn = PlayerPrefs.GetInt("eg_music", 1) == 1;
        bool sOn = PlayerPrefs.GetInt("eg_sfx", 1) == 1;
        SetupCamera();
        GameAudio.Init(gameObject, mOn, sOn);
        BuildUI();
        BuildMenu();
        if (!LoadGrid()) Seed();
        RefreshAll();
        tab = TabStart;                // игра начинается со стартового меню
        InitNet();
        ApplyTab();
        ApplyToggles();
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = C(255, 243, 224);
        if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
    }


    Egg Spawn(int c, int r, int level)
    {
        var e = Egg.Make(eggLayer, CellCenter(c, r), level, 1f);
        grid[c, r] = e;
        return e;
    }

    // яйца на поле, сохранённые в прошлый раз (строка из 25 цифр — уровни по строкам)
    bool LoadGrid()
    {
        string g = PlayerPrefs.GetString("eg_grid", "");
        if (g.Length != Cols * Rows) return false;
        bool any = false;
        for (int i = 0; i < g.Length; i++)
        {
            int l = g[i] - '0';
            if (l >= 1 && l <= MaxLevel) { Spawn(i % Cols, i / Cols, l); any = true; }
        }
        return any;
    }

    void Seed()
    {
        Spawn(0, 4, 1); Spawn(1, 4, 1);
        int n = 2, guard = 0;
        while (n < 9 && guard++ < 300)
        {
            int c = Random.Range(0, Cols), r = Random.Range(0, Rows);
            if (grid[c, r] != null) continue;
            Spawn(c, r, Random.value < 0.7f ? 1 : 2);
            n++;
        }
    }

    void NewEgg()
    {
        if (energy <= 0) { GameAudio.Deny(); return; }
        var free = new List<Vector2Int>();
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                if (grid[c, r] == null) free.Add(new Vector2Int(c, r));
        if (free.Count == 0) { GameAudio.Deny(); return; }
        var p = free[Random.Range(0, free.Count)];
        Spawn(p.x, p.y, Random.value < 0.12f ? 2 : 1);
        GameAudio.Spawn();
        energy--;
        AfterMove();
    }

    void AfterMove()
    {
        idle = 0f;
        RefreshTop(); RefreshQuest(); Save();
        CancelInvoke(nameof(CheckStuck));
        Invoke(nameof(CheckStuck), 0.5f);
    }

    bool FindPair(out Vector2Int a, out Vector2Int b)
    {
        for (int i = 0; i < Cols * Rows; i++)
        {
            var e1 = grid[i % Cols, i / Cols];
            if (e1 == null || e1.level >= MaxLevel) continue;
            for (int j = i + 1; j < Cols * Rows; j++)
            {
                var e2 = grid[j % Cols, j / Cols];
                if (e2 != null && e2.level == e1.level)
                {
                    a = new Vector2Int(i % Cols, i / Cols);
                    b = new Vector2Int(j % Cols, j / Cols);
                    return true;
                }
            }
        }
        a = b = default;
        return false;
    }

    void CheckStuck()
    {
        if (dragged != null || HasEmpty() || FindPair(out _, out _)) return;
        int minL = 99;
        foreach (var e in grid) if (e != null) minL = Mathf.Min(minL, e.level);
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                var e = grid[c, r];
                if (e == null || e.level != minL) continue;
                Burst(CellCenter(c, r), Palette[minL], 6);
                score += 5;
                Destroy(e.gameObject);
                grid[c, r] = null;
            }
        GameAudio.Stuck();
        FloatText("Нет ходов — убираем мелкие яйца", new Vector2(195, 344), 16, 300);
        AfterMove();
    }

    void ShowHint()
    {
        if (!FindPair(out var a, out var b)) { idle = 0f; return; }
        hintA = grid[a.x, a.y]; hintB = grid[b.x, b.y];
        hintA.SetHint(true); hintB.SetHint(true);
    }

    void ClearHint()
    {
        if (hintA != null) hintA.SetHint(false);
        if (hintB != null) hintB.SetHint(false);
        hintA = hintB = null;
    }

    // ---------- Ввод ----------
    void Update()
    {
        float dt = Time.deltaTime;

        // раскладка: вырез сверху, нижняя панель вкладок снизу, контент масштабируется по высоте
        float cs = Mathf.Max(0.01f, canvasRt.lossyScale.x);
        float topInset = Mathf.Max(0f, (Screen.height - (Screen.safeArea.y + Screen.safeArea.height)) / cs);
        float botInset = Mathf.Max(0f, Screen.safeArea.y / cs);
        nav.anchoredPosition = new Vector2(0, 14f + botInset);
        float avail = canvasRt.rect.height - topInset - (14f + botInset + 60f + 10f);
        float sc = Mathf.Clamp(avail / 700f, 0.6f, 1f);
        float extra = Mathf.Max(0f, avail - 700f * sc) * 0.25f;
        content.localScale = Vector3.one * sc;
        content.anchoredPosition = new Vector2(0, -(topInset + extra));

        if (banned) return;            // заблокированный игрок видит только экран блокировки

        if (resetArm > 0f)
        {
            resetArm -= dt;
            if (resetArm <= 0f) resetTxt.text = "Сбросить прогресс";
        }

        if (BackPressed() && tab != TabStart)
            SetTab(tab == TabProfile ? TabPlayers : tab == TabAvatar ? avatarReturnTab : TabStart);
        UpdatePlayers(dt);
        UpdateAccount(dt);
        UpdateFriends(dt);

        // восстановление энергии
        if (energy < MaxEnergy)
        {
            energyT += dt;
            if (energyT >= EnergyRegen) { energyT = 0f; energy++; RefreshTop(); }
        }

        Vector2 d = PointerDesign();
        bool down = PointerDown(), up = PointerUp();

        if (down)
        {
            Vector2 sp = PointerPos();
            foreach (var b in btns)
                if ((b.tab == -1 || b.tab == tab) && b.rt.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(b.rt, sp, null))
                { b.act(); down = false; break; }
        }

        if (down && tab == 0)
        {
            idle = 0f; ClearHint();
            if (BtnRect.Contains(d)) { btnPressed = true; btnRt.localScale = Vector3.one * 0.96f; }
            else if (dragged == null && ToCell(d, out var cell) && grid[cell.x, cell.y] != null)
            {
                dragged = grid[cell.x, cell.y];
                dragFrom = cell;
                grab = CellCenter(cell.x, cell.y) - d;
                dragged.dragging = true;
                dragged.transform.SetParent(dragLayer, false);
                GameAudio.Pick();
            }
        }

        if (dragged != null)
        {
            dragged.SetDragPos(d + grab);
            if (up) Drop(d);
        }

        if (up && btnPressed)
        {
            btnPressed = false;
            btnRt.localScale = Vector3.one;
            if (BtnRect.Contains(d)) NewEgg();
        }

        idle += dt;
        if (hintA == null && dragged == null && tab == 0 && idle > 6f) ShowHint();
    }

    void Drop(Vector2 d)
    {
        var e = dragged;
        dragged = null;
        e.dragging = false;
        e.transform.SetParent(eggLayer, false);

        if (!ToCell(d, out var t) || t == dragFrom)
        {
            e.home = CellCenter(dragFrom.x, dragFrom.y);
            GameAudio.Drop();
            return;
        }

        var other = grid[t.x, t.y];
        Vector2 pos = CellCenter(t.x, t.y);

        if (other == null) // переместить
        {
            GameAudio.Drop();
            grid[dragFrom.x, dragFrom.y] = null;
            grid[t.x, t.y] = e;
            e.home = pos;
        }
        else if (other.level == e.level && e.level < MaxLevel) // слияние
        {
            grid[dragFrom.x, dragFrom.y] = null;
            Destroy(e.gameObject);
            int nl = other.level + 1;
            other.Setup(nl);
            other.Pop();

            combo = (Time.time - lastMerge < 2.5f) ? Mathf.Min(combo + 1, 4) : 1;
            lastMerge = Time.time;
            int pts = nl * nl * 5 * combo;
            score += pts;
            FloatText("+" + pts + (combo > 1 ? "  x" + combo : ""), pos + new Vector2(0, -30), 20, 160);
            Burst(pos, Palette[nl], 10);
            GameAudio.Merge(nl, combo);

            if (zooCount < Zoo.Length && nl >= Zoo[zooCount].target) Hatch(t);
        }
        else // поменять местами
        {
            GameAudio.Drop();
            grid[dragFrom.x, dragFrom.y] = other;
            other.home = CellCenter(dragFrom.x, dragFrom.y);
            grid[t.x, t.y] = e;
            e.home = pos;
        }
        AfterMove();
    }

    // ---------- Вылупление и открытие животного ----------
    void Hatch(Vector2Int cell)
    {
        var egg = grid[cell.x, cell.y];
        Vector2 pos = CellCenter(cell.x, cell.y);
        grid[cell.x, cell.y] = null;
        Destroy(egg.gameObject);

        int idx = zooCount;
        zooCount++;
        score += 50;
        gems += 2;
        FloatText("+50", pos + new Vector2(0, -60), 22, 160);
        Burst(pos, Zoo[idx].color, 18);
        GameAudio.Hatch();
#if UNITY_IOS || UNITY_ANDROID
        if (GameAudio.SfxOn) Handheld.Vibrate();
#endif
        StartCoroutine(Reveal(idx, pos));
        RefreshTop(); RefreshQuest();
        KickNet();                     // сразу сообщить серверу о новом животном

        CancelInvoke(nameof(CheckPendingHatch));
        Invoke(nameof(CheckPendingHatch), 2.3f);
    }

    // если на поле уже лежит яйцо нужного уровня для следующего животного — оно вылупится
    void CheckPendingHatch()
    {
        if (zooCount >= Zoo.Length) return;
        int tgt = Zoo[zooCount].target;
        Egg best = null; int bc = 0, br = 0;
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                var e = grid[c, r];
                if (e != null && e != dragged && e.level >= tgt && (best == null || e.level < best.level))
                { best = e; bc = c; br = r; }
            }
        if (best != null) { Hatch(new Vector2Int(bc, br)); AfterMove(); }
    }

    IEnumerator Reveal(int idx, Vector2 pos)
    {
        var cr = MakeCreature(fxLayer, idx, 110f);
        cr.anchoredPosition = new Vector2(pos.x, -pos.y);

        float by = Mathf.Clamp(pos.y + 78f, 120f, 500f);
        var bn = TL(fxLayer, 70, by - 17, 250, 34, "Banner");
        Round(bn, C(59, 42, 74, 0.92f), 17);
        Label(bn, "Новое животное: " + Zoo[idx].name, 14, Color.white, TextAnchor.MiddleCenter, 0, 0, 250, 34);
        var cgp = bn.gameObject.AddComponent<CanvasGroup>();

        float t = 0f;
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            cr.localScale = Vector3.one * (t < 0.4f ? Back(t / 0.4f) : 1f + 0.03f * Mathf.Sin(t * 8f));
            yield return null;
        }

        Vector2 from = cr.anchoredPosition;
        Vector2 to = new Vector2(20f + 266f, -(530f + 30f));
        t = 0f;
        while (t < 0.55f)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, t / 0.55f);
            cr.anchoredPosition = Vector2.Lerp(from, to, u);
            cr.localScale = Vector3.one * Mathf.Lerp(1f, 32f / 110f, u);
            cgp.alpha = 1f - u;
            yield return null;
        }
        Destroy(cr.gameObject);
        Destroy(bn.gameObject);
        RefreshZoo();
    }

    // ---------- Ввод (старый и новый) ----------
    Vector2 PointerDesign()
    {
        Vector2 lp;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(content, PointerPos(), null, out lp))
            return new Vector2(lp.x + 195f, -lp.y);
        return new Vector2(-999, -999);
    }

#if ENABLE_INPUT_SYSTEM
    static Vector2 PointerPos() => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
    static bool PointerDown() => Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
    static bool PointerUp() => Pointer.current != null && Pointer.current.press.wasReleasedThisFrame;
#else
    static Vector2 PointerPos() => Input.mousePosition;
    static bool PointerDown() => Input.GetMouseButtonDown(0);
    static bool PointerUp() => Input.GetMouseButtonUp(0);
#endif
}
