using UnityEngine;

// ============================================================
//  Звук: WAV из Resources/Audio; если файла нет — звук синтезируется кодом
// ============================================================
public static class GameAudio
{
    const int SR = 22050;
    public static bool MusicOn = true, SfxOn = true;
    static AudioSource music, sfx;
    static AudioClip cClick, cPick, cDrop, cSpawn, cHatch, cBuy, cDeny, cStuck;
    static readonly AudioClip[] cMerge = new AudioClip[8];

    public static void Init(GameObject host, bool musicOn, bool sfxOn)
    {
        MusicOn = musicOn; SfxOn = sfxOn;

        cClick = Make("click", 0.05f, t => 0.5f * Sweep(t, 1500f, 1000f, 0.04f));
        cPick = Make("pick", 0.09f, t => 0.6f * Sweep(t, 520f, 780f, 0.08f));
        cDrop = Make("drop", 0.12f, t => 0.7f * Sweep(t, 260f, 140f, 0.10f));
        cSpawn = Make("spawn", 0.16f, t => 0.6f * Sweep(t, 320f, 980f, 0.14f));
        cDeny = Make("deny", 0.2f, t => 0.5f * Sweep(t, 220f, 150f, 0.18f));
        cBuy = Make("buy", 0.45f, t => 0.5f * (Bell(t, 988f, 0.25f) + Bell(t - 0.08f, 1319f, 0.3f)));
        cStuck = Make("stuck", 0.7f, t => 0.4f * (Bell(t, Hz(76), 0.3f) + Bell(t - 0.12f, Hz(72), 0.3f) + Bell(t - 0.24f, Hz(67), 0.4f)));

        for (int l = 2; l <= 7; l++)
        {
            int m = 60 + l * 2;
            cMerge[l] = Make("merge" + l, 0.45f, t => 0.45f * (Bell(t, Hz(m), 0.3f) + Bell(t - 0.07f, Hz(m + 7), 0.3f) + Bell(t - 0.14f, Hz(m + 12), 0.3f)));
        }

        int[] hm = { 72, 76, 79, 84, 88, 91 };
        cHatch = Make("hatch", 1.1f, t =>
        {
            float v = 0f;
            for (int i = 0; i < hm.Length; i++) v += Bell(t - i * 0.09f, Hz(hm[i]), 0.6f);
            return 0.3f * v;
        });

        cClick = Load("click", cClick); cPick = Load("pick", cPick); cDrop = Load("drop", cDrop);
        cSpawn = Load("spawn", cSpawn); cDeny = Load("deny", cDeny); cBuy = Load("buy", cBuy);
        cStuck = Load("stuck", cStuck); cHatch = Load("hatch", cHatch);
        for (int l = 2; l <= 7; l++) cMerge[l] = Load("merge_" + l, cMerge[l]);

        sfx = host.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.volume = 0.7f;

        music = host.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.volume = 0.32f;
        music.clip = Resources.Load<AudioClip>("Audio/music_loop");
        if (music.clip == null) music.clip = BuildMusic();
        else music.volume = 0.45f;
        music.mute = !MusicOn;
        music.Play();
    }

    static AudioClip Load(string name, AudioClip fallback)
    {
        var c = Resources.Load<AudioClip>("Audio/" + name);
        return c != null ? c : fallback;
    }

    public static void SetMusic(bool on) { MusicOn = on; if (music != null) music.mute = !on; }
    public static void SetSfx(bool on) { SfxOn = on; }

    static void Play(AudioClip c) { if (SfxOn && c != null && sfx != null) sfx.PlayOneShot(c); }
    public static void Click() { Play(cClick); }
    public static void Pick() { Play(cPick); }
    public static void Drop() { Play(cDrop); }
    public static void Spawn() { Play(cSpawn); }
    public static void Hatch() { Play(cHatch); }
    public static void Buy() { Play(cBuy); }
    public static void Deny() { Play(cDeny); }
    public static void Stuck() { Play(cStuck); }
    public static void Merge(int level, int combo) { Play(cMerge[Mathf.Clamp(level, 2, 7)]); }

    // ---------- синтез ----------
    static float Hz(int midi) { return 440f * Mathf.Pow(2f, (midi - 69) / 12f); }
    static float Tone(float t, float f) { return Mathf.Sin(2f * Mathf.PI * f * t); }

    static float Bell(float t, float f, float dur)
    {
        if (t < 0f || t > dur) return 0f;
        float env = Mathf.Exp(-5f * t / dur) * Mathf.Min(1f, t / 0.004f);
        return env * (Tone(t, f) + 0.3f * Tone(t, f * 2f) + 0.1f * Tone(t, f * 3f));
    }

    static float Sweep(float t, float f0, float f1, float dur)
    {
        if (t < 0f || t > dur) return 0f;
        float ph = 2f * Mathf.PI * (f0 * t + (f1 - f0) * t * t / (2f * dur));
        return Mathf.Exp(-4f * t / dur) * Mathf.Min(1f, t / 0.003f) * Mathf.Sin(ph);
    }

    static AudioClip Make(string name, float len, System.Func<float, float> f)
    {
        int n = (int)(len * SR);
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
        var c = AudioClip.Create(name, n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    static void Note(float[] d, float start, float dur, float f, float amp, float decay, float harm)
    {
        int s0 = (int)(start * SR), len = (int)((dur + 0.25f) * SR);
        for (int i = 0; i < len; i++)
        {
            int idx = s0 + i;
            if (idx >= d.Length) break;
            float t = i / (float)SR;
            float env = Mathf.Min(1f, t / 0.012f) * Mathf.Exp(-decay * t / dur);
            d[idx] += amp * env * (Mathf.Sin(2f * Mathf.PI * f * t) + harm * Mathf.Sin(4f * Mathf.PI * f * t));
        }
    }

    // Весёлая петля 10 секунд: бас + арпеджио + простая мелодия (До мажор: C – Am – F – G)
    static AudioClip BuildMusic()
    {
        const float beat = 0.6f;               // 100 ударов в минуту
        int n = (int)(beat * 16 * SR);
        var d = new float[n];

        int[] roots = { 48, 45, 41, 43 };
        int[][] chords =
        {
            new[] { 60, 64, 67, 72 }, new[] { 57, 60, 64, 69 },
            new[] { 53, 57, 60, 65 }, new[] { 55, 59, 62, 67 }
        };
        int[] pat = { 0, 1, 2, 3, 2, 1, 2, 1 };

        for (int bar = 0; bar < 4; bar++)
        {
            float t0 = bar * 4 * beat;
            Note(d, t0, 4 * beat, Hz(roots[bar]), 0.20f, 1.2f, 0.15f);
            for (int i = 0; i < 8; i++)
                Note(d, t0 + i * beat * 0.5f, beat * 0.9f, Hz(chords[bar][pat[i]]), 0.085f, 3.2f, 0.3f);
        }

        // мелодия: { доля, нота, длина в долях }
        float[,] mel =
        {
            { 0, 76, 1.5f }, { 2, 79, 1f }, { 3, 76, 1f },
            { 4, 74, 1.5f }, { 6, 72, 1f }, { 7, 69, 1f },
            { 8, 72, 1.5f }, { 10, 76, 1f }, { 11, 74, 1f },
            { 12, 71, 1f }, { 13, 74, 1f }, { 14, 79, 1.8f }
        };
        for (int i = 0; i < mel.GetLength(0); i++)
            Note(d, mel[i, 0] * beat, mel[i, 2] * beat, Hz((int)mel[i, 1]), 0.16f, 2.8f, 0.35f);

        float peak = 0.001f;
        foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
        for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;

        var c = AudioClip.Create("music", n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }
}
