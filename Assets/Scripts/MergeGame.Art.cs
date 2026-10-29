// Загрузка картинок и шрифта из Resources и запасные спрайты, нарисованные кодом.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    // ============================================================
    //  Ассеты из Assets/Resources (если файла нет — используется запасной вариант из кода)
    // ============================================================
    internal static readonly Sprite[] eggLvl = new Sprite[MaxLevel + 1];
    internal static readonly Sprite[] creatureSpr = new Sprite[12];
    internal static Sprite coinSpr, gemSpr, starSpr;

    static void LoadAssets()
    {
        var f = Resources.Load<Font>("Fonts/GameFont-ExtraBold");
        if (f != null) { font = f; fontStyle = FontStyle.Normal; }
        else
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Helvetica Neue", "Helvetica", "Roboto", "Noto Sans", "Segoe UI" }, 32);
            fontStyle = FontStyle.Bold;
        }

        eggSpr = MakeEggSprite();
        circleSpr = LoadSprite("circle", 64) ?? MakeCircleSprite();
        roundSpr = LoadSprite("panel", 200, 48) ?? MakeRoundSprite();
        ringSpr = LoadSprite("ring", 256) ?? MakeRingSprite();
        coinSpr = LoadSprite("coin", 128);
        gemSpr = LoadSprite("gem", 128);
        starSpr = LoadSprite("star", 64);
        for (int l = 1; l <= MaxLevel; l++) eggLvl[l] = LoadSprite("egg_" + l, 256);
        for (int i = 0; i < creatureSpr.Length; i++) creatureSpr[i] = LoadSprite("creature_" + i, 256);
    }

    static Sprite LoadSprite(string name, float ppu, float border = 0f)
    {
        var t = Resources.Load<Texture2D>("Art/" + name);
        if (t == null) return null;
        t.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), ppu, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    static void CoinIcon(Transform parent, float x, float y, float size)
    {
        if (coinSpr != null) { AddImg(CC(parent, x, y, size, size, "Coin"), coinSpr, Color.white); return; }
        AddImg(CC(parent, x, y, size, size, "CoinRing"), circleSpr, C(247, 213, 106));
        AddImg(CC(parent, x, y, size * 0.72f, size * 0.72f, "Coin"), circleSpr, C(242, 182, 50));
    }

    static void GemIcon(Transform parent, float x, float y, float size)
    {
        if (gemSpr != null) { AddImg(CC(parent, x, y, size, size, "Gem"), gemSpr, Color.white); return; }
        var gi = CC(parent, x, y, size * 0.75f, size * 0.75f, "Gem");
        gi.localRotation = Quaternion.Euler(0, 0, 45);
        Round(gi, C(91, 184, 232), 4);
    }

    // ============================================================
    //  Процедурные спрайты (картинки не нужны)
    // ============================================================
    static Sprite MakeEggSprite()
    {
        const int N = 128;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float nx = (x + 0.5f) / N * 2f - 1f, ny = (y + 0.5f) / N * 2f - 1f;
                float r = EggR(nx, ny);
                float a = Mathf.Clamp01((1f - r) * 18f);
                float hx = nx + 0.3f, hy = ny - 0.35f;
                float v = Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 1.4f));
                px[y * N + x] = new Color(v, v, v, a);
            }
        return ToSprite(px, N, 0);
    }

    static float EggR(float nx, float ny)
    {
        float k = 1f - 0.16f * ny;                 // верх уже, низ шире
        float ex = nx / (0.74f * k), ey = ny / 0.94f;
        return Mathf.Sqrt(ex * ex + ey * ey);
    }

    static Sprite MakeRingSprite() // пунктирный контур вокруг яйца
    {
        const int N = 128;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float nx = (x + 0.5f) / N * 2f - 1f, ny = (y + 0.5f) / N * 2f - 1f;
                float r = EggR(nx, ny);
                float ang = Mathf.Atan2(ny, nx) / (2f * Mathf.PI);
                float dash = Mathf.Repeat(ang * 14f, 1f) < 0.58f ? 1f : 0f;
                float a = Mathf.Clamp01((0.05f - Mathf.Abs(r - 0.9f)) * 30f) * dash;
                px[y * N + x] = new Color(1, 1, 1, a);
            }
        return ToSprite(px, N, 0);
    }

    static Sprite MakeCircleSprite()
    {
        const int N = 64;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float nx = (x + 0.5f) / N * 2f - 1f, ny = (y + 0.5f) / N * 2f - 1f;
                px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01((1f - Mathf.Sqrt(nx * nx + ny * ny)) * 14f));
            }
        return ToSprite(px, N, 0);
    }

    static Sprite MakeRoundSprite() // скруглённый прямоугольник с границами для растягивания (9-slice)
    {
        const int N = 128;
        const float rad = 48f;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - N / 2f) - (N / 2f - rad);
                float qy = Mathf.Abs(y + 0.5f - N / 2f) - (N / 2f - rad);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;
                px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
            }
        return ToSprite(px, N, rad);
    }

    static Sprite ToSprite(Color[] px, int n, float border)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        t.SetPixels(px);
        t.Apply();
        float ppu = border > 0f ? 200f : n;
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), ppu, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }
}
