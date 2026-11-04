// Механики поля: особые яйца, лёд и сундуки.
//
// • Особые яйца появляются иногда вместо обычного (кнопка «+ Новое яйцо»), а ещё — из колеса удачи,
//   ежедневного подарка и сундуков:
//     золотое  — соединяется как обычное, но даёт вдвое больше монет;
//     радужное — соединяется с яйцом любого уровня, получается яйцо на уровень выше;
//     бомбочка — нажать: лёд вокруг тает, мелкие яйца (до 3 уровня) вокруг превращаются в монеты.
// • Лёд: клетка подо льдом занята. Соединяйте яйца рядом (слева, справа, сверху, снизу) — лёд трескается
//   и тает, а за растаявший лёд дают монеты. В новой игре часть поля подо льдом — поле «растёт»;
//   после каждого нового животного одна свободная клетка иногда замерзает снова.
// • Сундук иногда появляется после соединения. Через 30 минут его можно открыть бесплатно,
//   раньше — за кристаллы (нажать два раза). Внутри монеты, кристаллы, а иногда особое яйцо.
//
// Сохранение поля — код в каждой клетке: 0 пусто, 1–11 обычное яйцо, 21–31 золотое (20 + уровень),
// 40 радужное, 41 бомбочка, 42 сундук. Лёд — отдельная строка из 25 цифр (прочность 0–2).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    const int CodeGold = 20, CodeRainbow = 40, CodeBomb = 41, CodeChest = 42;
    const int ChestSeconds = 30 * 60;

    readonly int[,] ice = new int[Cols, Rows];
    readonly RectTransform[,] iceRt = new RectTransform[Cols, Rows];
    RectTransform iceLayer;
    long chestAt;                 // когда сундук можно открыть бесплатно (unix-время), 0 — сундука нет
    float boardTick, chestArmT;
    Vector2Int chestArmCell = new Vector2Int(-1, -1);

    static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ============================================================
    //  Коды клеток
    // ============================================================
    static int CodeOf(Egg e)
    {
        if (e == null) return 0;
        switch (e.kind)
        {
            case EggKind.Gold: return CodeGold + e.level;
            case EggKind.Rainbow: return CodeRainbow;
            case EggKind.Bomb: return CodeBomb;
            case EggKind.Chest: return CodeChest;
            default: return e.level;
        }
    }

    // создать яйцо по коду; false — код непонятный (пусто)
    bool SpawnCode(int c, int r, int code)
    {
        if (code >= 1 && code <= MaxLevel) { Spawn(c, r, code); return true; }
        if (code > CodeGold && code <= CodeGold + MaxLevel) { SpawnKind(c, r, code - CodeGold, EggKind.Gold); return true; }
        if (code == CodeRainbow) { SpawnKind(c, r, 0, EggKind.Rainbow); return true; }
        if (code == CodeBomb) { SpawnKind(c, r, 0, EggKind.Bomb); return true; }
        if (code == CodeChest) { SpawnKind(c, r, 0, EggKind.Chest); return true; }
        return false;
    }

    Egg SpawnKind(int c, int r, int level, EggKind kind)
    {
        var e = Egg.Make(eggLayer, CellCenter(c, r), level, 1f, false, kind);
        grid[c, r] = e;
        return e;
    }

    bool CellFree(int c, int r) => grid[c, r] == null && ice[c, r] == 0;

    static bool Mergeable(Egg e) => e.kind == EggKind.Normal || e.kind == EggKind.Gold || e.kind == EggKind.Rainbow;

    // можно ли соединить a и b; nl — уровень результата, gold — монеты вдвое
    static bool CanMerge(Egg a, Egg b, out int nl, out bool gold)
    {
        nl = 0; gold = false;
        if (a == null || b == null || !Mergeable(a) || !Mergeable(b)) return false;
        if (a.kind == EggKind.Rainbow && b.kind == EggKind.Rainbow) return false;
        int lvl;
        if (a.kind == EggKind.Rainbow) lvl = b.level;
        else if (b.kind == EggKind.Rainbow) lvl = a.level;
        else if (a.level == b.level) lvl = a.level;
        else return false;
        if (lvl < 1 || lvl >= MaxLevel) return false;
        nl = lvl + 1;
        gold = a.kind == EggKind.Gold || b.kind == EggKind.Gold;
        return true;
    }

    // ============================================================
    //  Лёд
    // ============================================================
    void BuildIceLayer()
    {
        iceLayer = TL(content, 0, 0, 390, 700, "Ice");
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                var p = CellCenter(c, r);
                var cell = TL(iceLayer, p.x - 29, p.y - 29, 58, 58, "IceCell");
                Round(cell, C(196, 230, 250, 0.95f), 16);
                Round(TL(cell, 4, 4, 50, 22, "Shine"), C(235, 248, 255, 0.9f), 10);
                // трещины — видны, когда лёд уже треснул (прочность 1)
                var cracks = TL(cell, 0, 0, 58, 58, "Cracks");
                for (int i = 0; i < 3; i++)
                {
                    var l = CC(cracks, -6 + i * 7, -4 + (i % 2) * 6, 3, 26 - i * 5, "Crack");
                    l.localRotation = Quaternion.Euler(0, 0, 35 - i * 40);
                    AddImg(l, null, C(120, 170, 210, 0.9f));
                }
                AddImg(CC(cell, 12, 10, 8, 8, "Spark"), starSpr != null ? starSpr : circleSpr, Color.white);
                iceRt[c, r] = cell;
            }
    }

    void RefreshIce()
    {
        if (iceLayer == null) return;
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                iceRt[c, r].gameObject.SetActive(ice[c, r] > 0);
                iceRt[c, r].Find("Cracks").gameObject.SetActive(ice[c, r] == 1);
            }
    }

    // соединили яйца в клетке t — лёд рядом трескается
    void HitIceAround(Vector2Int t)
    {
        Vector2Int[] dirs = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        foreach (var d in dirs)
        {
            var n = t + d;
            if (n.x < 0 || n.x >= Cols || n.y < 0 || n.y >= Rows || ice[n.x, n.y] == 0) continue;
            ice[n.x, n.y]--;
            if (ice[n.x, n.y] == 0) Thaw(n);
            else Burst(CellCenter(n.x, n.y), C(196, 230, 250), 5);
        }
        RefreshIce();
    }

    void Thaw(Vector2Int n)
    {
        ice[n.x, n.y] = 0;
        var p = CellCenter(n.x, n.y);
        Burst(p, C(160, 215, 245), 12);
        int coins = 20;
        bool gem = UnityEngine.Random.value < 0.25f;
        score += coins;
        if (gem) gems++;
        FloatText("+" + coins + (gem ? " и кристалл" : ""), p + new Vector2(0, -26), 16, 200);
        QuestAdd(QuestKind.Ice);
    }

    // клетки подо льдом в начале новой игры (поле «растёт» по мере игры)
    void SeedIce()
    {
        Array.Clear(ice, 0, ice.Length);
        Vector2Int[] cells = { new Vector2Int(0, 0), new Vector2Int(4, 0), new Vector2Int(0, 2), new Vector2Int(4, 2), new Vector2Int(1, 0), new Vector2Int(3, 0) };
        foreach (var p in cells) ice[p.x, p.y] = 2;
        RefreshIce();
    }

    // после нового животного иногда замерзает одна свободная клетка (если места хватает)
    void FreezeRandomCell()
    {
        int iced = 0;
        foreach (var v in ice) if (v > 0) iced++;
        var free = FreeCells();
        if (iced >= 4 || free.Count < 6 || UnityEngine.Random.value > 0.5f) return;
        var p = free[UnityEngine.Random.Range(0, free.Count)];
        ice[p.x, p.y] = 2;
        RefreshIce();
        Toast("Клетка замёрзла — соединяйте яйца рядом, чтобы растопить");
    }

    // ============================================================
    //  Особые яйца
    // ============================================================
    // вид нового яйца с кнопки: иногда особое
    EggKind RollNewEggKind()
    {
        float v = UnityEngine.Random.value;
        if (v < 0.03f) return EggKind.Rainbow;
        if (v < 0.09f) return EggKind.Gold;
        if (v < 0.12f) return EggKind.Bomb;
        return EggKind.Normal;
    }

    // положить особое яйцо в свободную клетку (награды); false — нет места
    bool GiveSpecial(EggKind kind, int level = 1)
    {
        var free = FreeCells();
        if (free.Count == 0) return false;
        var p = free[UnityEngine.Random.Range(0, free.Count)];
        SpawnKind(p.x, p.y, kind == EggKind.Gold || kind == EggKind.Normal ? Mathf.Max(1, level) : 0, kind);
        return true;
    }

    void Explode(Vector2Int cell)
    {
        var bomb = grid[cell.x, cell.y];
        var p0 = CellCenter(cell.x, cell.y);
        grid[cell.x, cell.y] = null;
        Destroy(bomb.gameObject);
        Burst(p0, C(255, 170, 70), 18);
        GameAudio.Merge(6, 2);

        int gained = 0, cleared = 0;
        for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
            {
                int c = cell.x + dc, r = cell.y + dr;
                if ((dc == 0 && dr == 0) || c < 0 || c >= Cols || r < 0 || r >= Rows) continue;
                if (ice[c, r] > 0) { Thaw(new Vector2Int(c, r)); cleared++; continue; }
                var e = grid[c, r];
                if (e != null && (e.kind == EggKind.Normal || e.kind == EggKind.Gold) && e.level <= 3)
                {
                    int pts = e.level * e.level * 5 * (e.kind == EggKind.Gold ? 2 : 1);
                    gained += pts; cleared++;
                    Burst(CellCenter(c, r), Palette[e.level], 6);
                    grid[c, r] = null;
                    Destroy(e.gameObject);
                }
            }
        score += gained;
        FloatText(cleared > 0 ? "Бах! +" + gained : "Бах!", p0 + new Vector2(0, -30), 20, 200);
        RefreshIce();
        QuestAdd(QuestKind.Special);
    }

    // ============================================================
    //  Сундуки
    // ============================================================
    bool HasChest()
    {
        foreach (var e in grid) if (e != null && e.kind == EggKind.Chest) return true;
        return false;
    }

    void MaybeSpawnChest(int mergedLevel)
    {
        if (mergedLevel < 4 || HasChest() || UnityEngine.Random.value > 0.05f) return;
        var free = FreeCells();
        if (free.Count < 3) return;
        var p = free[UnityEngine.Random.Range(0, free.Count)];
        SpawnKind(p.x, p.y, 0, EggKind.Chest);
        chestAt = NowUnix() + ChestSeconds;
        PlayerPrefs.SetString("eg_chest_at", chestAt.ToString());
        Toast("Появился сундук! Откроется через 30 минут");
        GameAudio.Buy();
    }

    int ChestLeft() => (int)Math.Max(0, chestAt - NowUnix());

    int ChestGemPrice() => Mathf.Clamp(Mathf.CeilToInt(ChestLeft() / 600f), 1, 3);   // 1 кристалл за каждые 10 минут, не больше 3

    void TapChest(Vector2Int cell)
    {
        if (ChestLeft() <= 0) { OpenChest(cell); return; }
        int price = ChestGemPrice();
        if (chestArmT > 0f && chestArmCell == cell)
        {
            chestArmT = 0f;
            if (gems < price) { GameAudio.Deny(); Toast("Не хватает кристаллов"); return; }
            gems -= price;
            OpenChest(cell);
            return;
        }
        chestArmT = 3f;
        chestArmCell = cell;
        GameAudio.Click();
        Toast("Откроется через " + (ChestLeft() / 60 + 1) + " мин. Нажмите ещё раз — открыть сейчас за " + price + " крист.");
    }

    void OpenChest(Vector2Int cell)
    {
        var chest = grid[cell.x, cell.y];
        var p = CellCenter(cell.x, cell.y);
        grid[cell.x, cell.y] = null;
        Destroy(chest.gameObject);
        chestAt = 0;
        PlayerPrefs.SetString("eg_chest_at", "0");

        int coins = UnityEngine.Random.Range(10, 26) * 10;
        int gemsAdd = UnityEngine.Random.Range(1, 3);
        score += coins; gems += gemsAdd;
        string extra = "";
        float roll = UnityEngine.Random.value;
        if (roll < 0.15f) { SpawnKind(cell.x, cell.y, 0, EggKind.Rainbow); extra = " и радужное яйцо"; }
        else if (roll < 0.35f) { SpawnKind(cell.x, cell.y, 2, EggKind.Gold); extra = " и золотое яйцо"; }
        Burst(p, C(255, 205, 90), 20);
        GameAudio.Hatch();
        FloatText("+" + coins, p + new Vector2(0, -36), 20, 160);
        Toast("Сундук: " + coins + " монет, " + gemsAdd + " крист." + extra);
        QuestAdd(QuestKind.Chest);
        AfterMove();
    }

    // ============================================================
    //  Каждый кадр: таймер сундука
    // ============================================================
    void UpdateBoard(float dt)
    {
        if (chestArmT > 0f) chestArmT -= dt;
        boardTick -= dt;
        if (boardTick > 0f) return;
        boardTick = 0.5f;
        foreach (var e in grid)
        {
            if (e == null || e.kind != EggKind.Chest) continue;
            if (chestAt <= 0) { chestAt = NowUnix() + ChestSeconds; PlayerPrefs.SetString("eg_chest_at", chestAt.ToString()); }
            int left = ChestLeft();
            e.SetTimer(left <= 0 ? "Открыть!" : (left / 60) + ":" + (left % 60).ToString("00"));
        }
    }

    // ============================================================
    //  Сохранение льда и сундука
    // ============================================================
    string IceString()
    {
        var sb = new System.Text.StringBuilder(Cols * Rows);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                sb.Append((char)('0' + ice[c, r]));
        return sb.ToString();
    }

    void SetIce(string s)
    {
        Array.Clear(ice, 0, ice.Length);
        if (s != null && s.Length == Cols * Rows)
            for (int i = 0; i < s.Length; i++)
                ice[i % Cols, i / Cols] = Mathf.Clamp(s[i] - '0', 0, 2);
        // лёд не может лежать на яйце
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                if (grid[c, r] != null) ice[c, r] = 0;
        RefreshIce();
    }

    int[] IceArray()
    {
        var a = new int[Cols * Rows];
        for (int i = 0; i < a.Length; i++) a[i] = ice[i % Cols, i / Cols];
        return a;
    }

    void SetIceArray(int[] a)
    {
        var sb = new System.Text.StringBuilder();
        if (a != null && a.Length == Cols * Rows) foreach (var v in a) sb.Append((char)('0' + Mathf.Clamp(v, 0, 2)));
        SetIce(sb.ToString());
    }

    void LoadChestTime()
    {
        long.TryParse(PlayerPrefs.GetString("eg_chest_at", "0"), out chestAt);
    }
}
