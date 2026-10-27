using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  Яйцо: объём, блик, крапинки, номер уровня, плавное движение
// ============================================================
public class Egg : MonoBehaviour
{
    public int level;
    public Vector2 home;       // «дизайн»-координаты центра ячейки
    public bool dragging;

    RectTransform rt, gloss;
    Image body, ring;
    Text num;
    readonly Image[] specks = new Image[3];
    float k = 1f, pop, appear;

    internal static Egg Make(Transform parent, Vector2 home, int level, float k, bool instant = false)
    {
        var rt = MergeGame.NewRt("Egg", parent);
        var e = rt.gameObject.AddComponent<Egg>();
        e.Build(rt, home, level, k, instant);
        return e;
    }

    void Build(RectTransform root, Vector2 homePos, int lvl, float scale, bool instant)
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

        Setup(lvl);
    }

    public void Setup(int lvl)
    {
        level = lvl;
        Color tint = MergeGame.Palette[lvl];
        var painted = MergeGame.eggLvl[lvl];
        body.sprite = painted != null ? painted : MergeGame.eggSpr;
        body.color = painted != null ? Color.white : tint;
        gloss.gameObject.SetActive(painted == null);
        foreach (var s in specks) s.gameObject.SetActive(painted == null);
        num.text = lvl.ToString();
        Color sp = Color.Lerp(tint, new Color(0.23f, 0.16f, 0.29f), 0.3f);
        sp.a = 0.5f;
        foreach (var s in specks) s.color = sp;
    }

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
    }
}
