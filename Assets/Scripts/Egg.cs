using UnityEngine;
using UnityEngine.UI;

// Вид яйца на поле
public enum EggKind
{
    Normal,     // обычное яйцо уровня 1–11
    Gold,       // золотое: соединяется как обычное, но даёт вдвое больше монет
    Rainbow,    // радужное: соединяется с яйцом любого уровня (результат — уровень выше)
    Bomb,       // бомбочка: нажать — растапливает лёд и превращает мелкие яйца вокруг в монеты
    Chest,      // сундук: открывается через 30 минут или сразу за кристаллы
}

// ============================================================
//  Яйцо: объём, блик, крапинки, номер уровня, плавное движение
// ============================================================
public class Egg : MonoBehaviour
{
    public int level;          // для радужного, бомбочки и сундука — 0
    public EggKind kind;
    public Vector2 home;       // «дизайн»-координаты центра ячейки
    public bool dragging;

    RectTransform rt, gloss, extra;
    Image body, ring;
    Text num;
    readonly Image[] specks = new Image[3];
    float k = 1f, pop, appear;

    internal static Egg Make(Transform parent, Vector2 home, int level, float k, bool instant = false, EggKind kind = EggKind.Normal)
    {
        var rt = MergeGame.NewRt("Egg", parent);
        var e = rt.gameObject.AddComponent<Egg>();
        e.Build(rt, home, level, k, instant, kind);
        return e;
    }

    void Build(RectTransform root, Vector2 homePos, int lvl, float scale, bool instant, EggKind kd)
    {
        rt = root; k = scale; home = homePos;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(56f * k, 56f * k);
        rt.anchoredPosition = new Vector2(home.x, -home.y);
        appear = instant ? 1f : 0f;

        MergeGame.AddImg(MergeGame.CC(rt, 0, -4f * k, 62f * k, 62f * k, "Shadow"), MergeGame.eggSpr, new Color(0.23f, 0.16f, 0.29f, 0.14f));
        body = MergeGame.AddImg(MergeGame.CC(rt, 0, 0, 62f * k, 62f * k, "Body"), MergeGame.eggSpr, Color.white);

        gloss = MergeGame.CC(rt, -9f * k, 12f * k, 9f * k, 14f * k, "Gloss");
        gloss.localRotation = Quaternion.Euler(0, 0, 20);
        MergeGame.AddImg(gloss, MergeGame.circleSpr, new Color(1, 1, 1, 0.55f));

        float[,] sp = { { 11, 5, 4.5f }, { 7, -7, 3.5f }, { -8, -15, 3f } };
        for (int i = 0; i < 3; i++)
            specks[i] = MergeGame.AddImg(MergeGame.CC(rt, sp[i, 0] * k, sp[i, 1] * k, sp[i, 2] * k, sp[i, 2] * k, "Speck"),
                MergeGame.circleSpr, Color.white);

        num = MergeGame.AddText(MergeGame.CC(rt, 0, -1f * k, 44f * k, 44f * k, "Num"),
            "", Mathf.RoundToInt(18f * k), new Color(59 / 255f, 42 / 255f, 74 / 255f), TextAnchor.MiddleCenter);

        Setup(lvl, kd);
    }

    public void Setup(int lvl) => Setup(lvl, EggKind.Normal);

    public void Setup(int lvl, EggKind kd)
    {
        kind = kd;
        level = kd == EggKind.Normal || kd == EggKind.Gold ? lvl : 0;
        if (extra != null) { Destroy(extra.gameObject); extra = null; }
        body.rectTransform.sizeDelta = new Vector2(62f * k, 62f * k);
        body.type = Image.Type.Simple;
        num.fontSize = Mathf.RoundToInt(18f * k);
        num.rectTransform.anchoredPosition = new Vector2(0, -1f * k);
        num.color = new Color(59 / 255f, 42 / 255f, 74 / 255f);

        if (kd == EggKind.Normal || kd == EggKind.Gold)
        {
            Color tint = MergeGame.Palette[level];
            var painted = MergeGame.eggLvl[level];
            body.sprite = painted != null ? painted : MergeGame.eggSpr;
            body.color = painted != null ? Color.white : tint;
            gloss.gameObject.SetActive(painted == null);
            foreach (var s in specks) s.gameObject.SetActive(painted == null);
            num.text = level.ToString();
            Color sp = Color.Lerp(tint, new Color(0.23f, 0.16f, 0.29f), 0.3f);
            sp.a = 0.5f;
            foreach (var s in specks) s.color = sp;
            if (kd == EggKind.Gold)
            {
                // золотое сияние позади и звёздочка сбоку
                extra = MergeGame.CC(rt, 0, 0, 74f * k, 74f * k, "Gold");
                extra.SetAsFirstSibling();
                MergeGame.AddImg(extra, MergeGame.circleSpr, new Color(1f, 0.82f, 0.3f, 0.55f));
                var st = MergeGame.CC(extra, 22f * k, 22f * k, 22f * k, 22f * k, "Star");
                MergeGame.AddImg(st, MergeGame.starSpr != null ? MergeGame.starSpr : MergeGame.circleSpr, new Color(1f, 0.78f, 0.15f));
            }
            return;
        }

        gloss.gameObject.SetActive(true);
        foreach (var s in specks) s.gameObject.SetActive(false);
        num.text = "";
        body.sprite = MergeGame.eggSpr;
        if (kd == EggKind.Rainbow)
        {
            body.color = Color.white;             // цвет переливается в Update
            foreach (var s in specks) { s.gameObject.SetActive(true); s.color = new Color(1, 1, 1, 0.7f); }
            extra = MergeGame.CC(rt, 0, -1f * k, 26f * k, 26f * k, "Star");
            MergeGame.AddImg(extra, MergeGame.starSpr != null ? MergeGame.starSpr : MergeGame.circleSpr, Color.white);
        }
        else if (kd == EggKind.Bomb)
        {
            body.sprite = MergeGame.circleSpr;
            body.rectTransform.sizeDelta = new Vector2(52f * k, 52f * k);
            body.color = new Color(0.26f, 0.23f, 0.31f);
            extra = MergeGame.CC(rt, 14f * k, 22f * k, 14f * k, 14f * k, "Fuse");
            MergeGame.AddImg(extra, MergeGame.circleSpr, new Color(1f, 0.6f, 0.2f));      // фитиль мигает в Update
            num.text = "!";
            num.color = Color.white;
        }
        else // сундук
        {
            gloss.gameObject.SetActive(false);
            body.sprite = MergeGame.roundSpr;
            body.type = Image.Type.Sliced;
            body.pixelsPerUnitMultiplier = 24f / 10f;
            body.rectTransform.sizeDelta = new Vector2(52f * k, 42f * k);
            body.color = new Color(0.72f, 0.47f, 0.28f);
            extra = MergeGame.CC(rt, 0, 4f * k, 52f * k, 7f * k, "Band");
            MergeGame.AddImg(extra, null, new Color(1f, 0.8f, 0.3f));
            MergeGame.AddImg(MergeGame.CC(extra, 0, -1f * k, 11f * k, 13f * k, "Lock"), MergeGame.circleSpr, new Color(1f, 0.86f, 0.4f));
            num.fontSize = Mathf.RoundToInt(11f * k);
            num.rectTransform.anchoredPosition = new Vector2(0, -32f * k);       // таймер под сундуком
        }
    }

    // текст под сундуком: сколько осталось
    public void SetTimer(string s) { if (kind == EggKind.Chest) num.text = s; }

    public void Pop() { pop = 1f; }

    public void SetDragPos(Vector2 d) { rt.anchoredPosition = new Vector2(d.x, -d.y); }

    public void SetHint(bool on)
    {
        if (on && ring == null)
        {
            var r = MergeGame.CC(rt, 0, 0, 62f * k * 1.25f, 62f * k * 1.25f, "Hint");
            ring = MergeGame.AddImg(r, MergeGame.ringSpr, new Color(0.79f, 0.24f, 0.13f));
        }
        if (ring != null) ring.gameObject.SetActive(on);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (!dragging)
        {
            Vector2 target = new Vector2(home.x, -home.y);
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, target, 1f - Mathf.Exp(-18f * dt));
        }
        appear = Mathf.MoveTowards(appear, 1f, dt * 4f);
        pop = Mathf.MoveTowards(pop, 0f, dt * 3.5f);
        float s = MergeGame.Back(appear) * (1f + 0.25f * Mathf.Sin(pop * Mathf.PI)) * (dragging ? 1.12f : 1f);
        rt.localScale = Vector3.one * s;
        if (ring != null && ring.gameObject.activeSelf)
            ring.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 6f));

        if (kind == EggKind.Rainbow)
            body.color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.25f + home.x * 0.003f, 1f), 0.45f, 1f);
        else if (kind == EggKind.Bomb && extra != null)
            extra.localScale = Vector3.one * (0.8f + 0.3f * Mathf.Abs(Mathf.Sin(Time.time * 7f)));
        else if (kind == EggKind.Gold && extra != null)
            extra.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(Time.time * 3f));
    }
}
