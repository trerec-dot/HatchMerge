// Эффекты (всплывающие очки, искры) и отрисовка животных.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    // ---------- Эффекты ----------
    void FloatText(string s, Vector2 pos, int size, float w, Transform parent = null)
    {
        var rt = TL(parent != null ? parent : fxLayer, pos.x - w / 2f, pos.y - 15f, w, 30f, "Float");
        var t = AddText(rt, s, size, Red, TextAnchor.MiddleCenter);
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = Color.white; o.effectDistance = new Vector2(1.5f, -1.5f);
        rt.gameObject.AddComponent<Floater>().Init(t);
    }

    void Burst(Vector2 pos, Color col, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var rt = CC(fxLayer, 0, 0, 9, 9, "Spark");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(pos.x, -pos.y);
            bool st = starSpr != null && i % 2 == 0;
            if (st) { rt.sizeDelta = new Vector2(16, 16); rt.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 90f)); }
            var im = AddImg(rt, st ? starSpr : circleSpr, st ? Color.Lerp(col, Color.white, 0.4f) : col);
            rt.gameObject.AddComponent<Spark>().Init(im, col, Random.insideUnitCircle.normalized * Random.Range(60f, 150f));
        }
    }

    // ---------- Животное (рисуется кодом из кружков) ----------
    static RectTransform MakeCreature(Transform parent, int idx, float size)
    {
        var root = NewRt("Creature", parent);
        root.anchorMin = root.anchorMax = new Vector2(0, 1);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(size, size);

        if (creatureSpr[idx] != null)
        {
            var pic = CC(root, 0, 0, size * 1.3f, size * 1.3f, "Picture");
            pic.anchoredPosition = new Vector2(0, size * 0.06f);
            AddImg(pic, creatureSpr[idx], Color.white).preserveAspect = true;
            return root;
        }

        var z = Zoo[idx];
        Color body = z.color;
        Color dark = Color.Lerp(body, Ink, 0.25f);
        Color light = Color.Lerp(body, Color.white, 0.55f);

        void Blob(float x, float y, float w, float h, Color col, float rot = 0f)
        {
            var r = CC(root, x * size, y * size, w * size, h * size);
            r.localRotation = Quaternion.Euler(0, 0, rot);
            AddImg(r, circleSpr, col);
        }

        switch (z.look)
        {
            case 0: Blob(-0.22f, 0.3f, 0.3f, 0.3f, body); Blob(0.2f, 0.32f, 0.34f, 0.34f, body); break;
            case 1: Blob(-0.2f, 0.4f, 0.13f, 0.24f, light, 20); Blob(0.2f, 0.4f, 0.13f, 0.24f, light, -20); break;
            case 2: Blob(-0.22f, -0.4f, 0.14f, 0.28f, dark); Blob(0f, -0.44f, 0.14f, 0.3f, dark); Blob(0.22f, -0.4f, 0.14f, 0.28f, dark); break;
            case 3: Blob(-0.27f, 0.36f, 0.24f, 0.24f, dark); Blob(0.27f, 0.36f, 0.24f, 0.24f, dark); break;
            case 4: Blob(-0.3f, 0.32f, 0.2f, 0.28f, dark, 25); Blob(0.3f, 0.32f, 0.2f, 0.28f, dark, -25); break;
            case 5: Blob(-0.3f, 0.34f, 0.16f, 0.26f, light, 25); Blob(0.3f, 0.34f, 0.16f, 0.26f, light, -25); break;
            case 6: Blob(-0.17f, 0.5f, 0.16f, 0.42f, body); Blob(0.17f, 0.5f, 0.16f, 0.42f, body); break;
            default: Blob(0f, 0.5f, 0.1f, 0.32f, C(255, 217, 115)); Blob(-0.28f, 0.34f, 0.18f, 0.18f, dark); Blob(0.28f, 0.34f, 0.18f, 0.18f, dark); break;
        }

        Blob(0f, -0.04f, 0.78f, 0.78f, body);                 // тело
        Blob(0f, -0.22f, 0.42f, 0.3f, new Color(1, 1, 1, 0.45f)); // пузико
        Blob(-0.15f, 0.04f, 0.09f, 0.11f, Ink);               // глаза
        Blob(0.15f, 0.04f, 0.09f, 0.11f, Ink);
        Blob(-0.27f, -0.09f, 0.12f, 0.08f, C(245, 143, 168, 0.6f)); // щёчки
        Blob(0.27f, -0.09f, 0.12f, 0.08f, C(245, 143, 168, 0.6f));
        return root;
    }
}
