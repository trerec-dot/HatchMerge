// 3D-галерея животных. Модели собираются кодом из объёмных капель (сфер разной формы) в стиле
// картинок игры: тело, животик, глазки с бликами, щёчки, лапки и у каждого — свои ушки, рожки,
// щупальца или хвостик. Отдельная камера рисует модель в текстуру, которая показана в интерфейсе.
//
// • Открыть: «Зоопарк» → нажать на животное.
// • Крутить пальцем (или мышью); если не трогать — модель медленно поворачивается сама.
// • Стрелки листают всех 8 животных; ещё не открытые показаны тёмным силуэтом.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public partial class MergeGame
{
    const int TabGallery = 9;
    const int GalleryLayer = 31;
    static readonly Vector3 Stage = new Vector3(1000f, 0f, 0f);   // далеко от всего остального

    // Части моделей. Строка — одна капля: цвет X Y Z ширина высота глубина наклон(°).
    // Цвета: B тело, D темнее тела, L светлее тела, W белый, K глаза, P щёчки, Y золотой,
    //        N тёмно-синий, A угольный, R красный, O оранжевый, S коричневые иголки.
    // Общие части (тело, животик, глаза, щёчки) добавляются отдельно. «-» в конце строки — без лапок.
    static readonly string[] Look3D =
    {
        // 0 облачко: три пушистых облачка на макушке
        "B -0.46 0.62 0.05 0.66 0.62 0.6 0; B 0.42 0.66 0.05 0.74 0.7 0.66 0; B 0 0.82 0.15 0.62 0.58 0.56 0",
        // 1 дракончик: светлые рожки, крылышки, хвост
        "L -0.36 0.84 0.05 0.24 0.5 0.24 22; L 0.36 0.84 0.05 0.24 0.5 0.24 -22;" +
        " D -0.9 0.12 0.35 0.56 0.36 0.12 35; D 0.9 0.12 0.35 0.56 0.36 0.12 -35; B 0.55 -0.62 0.62 0.5 0.32 0.6 -20",
        // 2 медузка: три щупальца снизу и шапочка
        "D -0.42 -0.88 0 0.26 0.62 0.26 8; D 0 -0.95 0 0.26 0.68 0.26 0; D 0.42 -0.88 0 0.26 0.62 0.26 -8;" +
        " L 0 0.6 0 0.7 0.3 0.6 0;-",
        // 3 лисёнок: тёмные ушки, пушистый хвост со светлым кончиком
        "D -0.54 0.74 0.05 0.44 0.52 0.24 12; D 0.54 0.74 0.05 0.44 0.52 0.24 -12;" +
        " B 0.62 -0.52 0.62 0.62 0.42 0.46 -40; W 0.9 -0.3 0.74 0.32 0.3 0.3 -40",
        // 4 котик: острые ушки и хвостик
        "D -0.6 0.66 0.05 0.38 0.56 0.24 25; D 0.6 0.66 0.05 0.38 0.56 0.24 -25; D 0.7 -0.5 0.55 0.26 0.7 0.26 -40",
        // 5 совёнок: светлые пёрышки-ушки и крылья
        "L -0.6 0.7 0.05 0.3 0.52 0.2 25; L 0.6 0.7 0.05 0.3 0.52 0.2 -25;" +
        " L -0.8 -0.12 0.05 0.36 0.7 0.36 15; L 0.8 -0.12 0.05 0.36 0.7 0.36 -15",
        // 6 зайка: длинные ушки с розовой серединкой, хвостик-пуховка
        "B -0.34 1.02 0.05 0.32 0.86 0.24 6; B 0.34 1.02 0.05 0.32 0.86 0.24 -6;" +
        " P -0.34 1.02 -0.07 0.16 0.6 0.08 6; P 0.34 1.02 -0.07 0.16 0.6 0.08 -6; W 0 -0.45 0.72 0.36 0.36 0.36 0",
        // 7 единорожек: золотой рог, ушки, светлая грива
        "Y 0 1.02 -0.12 0.18 0.66 0.18 0; D -0.56 0.68 0.05 0.36 0.36 0.24 0; D 0.56 0.68 0.05 0.36 0.36 0.24 0;" +
        " L 0 0.72 0.42 0.5 0.5 0.4 0; L 0 0.38 0.62 0.44 0.5 0.36 0",
        // 8 пингвинчик: тёмно-синяя спинка-«фрак», крылышки, оранжевые клюв и лапки
        "N 0 0.02 0.12 1.66 1.64 1.36 0; N -0.86 -0.12 0.08 0.24 0.74 0.34 20; N 0.86 -0.12 0.08 0.24 0.74 0.34 -20;" +
        " O 0 -0.05 -0.72 0.22 0.14 0.2 0; O -0.32 -0.9 -0.22 0.42 0.18 0.52 0; O 0.32 -0.9 -0.22 0.42 0.18 0.52 0;-",
        // 9 ёжик: иголки на макушке и спинке, светлая мордочка, носик
        "S -0.5 0.56 0.32 0.3 0.62 0.3 32; S 0 0.78 0.34 0.3 0.66 0.3 0; S 0.5 0.56 0.32 0.3 0.62 0.3 -32;" +
        " S -0.78 0.12 0.36 0.3 0.6 0.3 70; S 0.78 0.12 0.36 0.3 0.6 0.3 -70; S -0.32 0.42 0.66 0.3 0.6 0.3 22;" +
        " S 0.32 0.42 0.66 0.3 0.6 0.3 -22; S 0 0.08 0.76 0.42 0.5 0.42 0; S -0.55 -0.3 0.6 0.3 0.5 0.3 55; S 0.55 -0.3 0.6 0.3 0.5 0.3 -55;" +
        " L 0 -0.1 -0.4 1.1 0.95 0.6 0; K 0 -0.02 -0.74 0.12 0.09 0.08 0",
        // 10 пандочка: угольные ушки, пятна вокруг глаз, лапки
        "A -0.56 0.7 0.05 0.4 0.4 0.26 0; A 0.56 0.7 0.05 0.4 0.4 0.26 0; A -0.3 0.07 -0.585 0.36 0.44 0.12 22;" +
        " A 0.3 0.07 -0.585 0.36 0.44 0.12 -22; A -0.66 -0.4 -0.22 0.36 0.52 0.36 20; A 0.66 -0.4 -0.22 0.36 0.52 0.36 -20;" +
        " A -0.34 -0.88 -0.12 0.44 0.24 0.5 0; A 0.34 -0.88 -0.12 0.44 0.24 0.5 0;-",
        // 11 фениксик: огненный хохолок, крылышки-язычки пламени, хвост, клювик
        "Y 0 1.0 0.05 0.3 0.66 0.3 0; R -0.26 0.88 0.1 0.24 0.54 0.24 26; R 0.26 0.88 0.1 0.24 0.54 0.24 -26;" +
        " Y -0.9 0.02 0.15 0.52 0.32 0.14 40; Y 0.9 0.02 0.15 0.52 0.32 0.14 -40; R 0 -0.36 0.74 0.42 0.32 0.62 0;" +
        " Y 0 -0.12 0.86 0.3 0.24 0.42 0; O 0 -0.05 -0.72 0.18 0.12 0.18 0",
    };

    static Mesh sphereMesh;
    static Material toonMat;
    MaterialPropertyBlock mpb;

    RectTransform galleryPanel, galView;
    readonly RectTransform[] zooHits = new RectTransform[12];
    Text galName, galInfo, galCount;
    Camera galCam;
    RenderTexture galRt;
    Transform galRoot, galModel, galPedestal;
    readonly List<Transform> galEyes = new List<Transform>();
    int galIndex;
    float galYaw = -25f, galIdle, galBlink = 2f, galPitch;
    bool galDragging;
    Vector2 galLast;

    // ============================================================
    //  Интерфейс
    // ============================================================
    void BuildGallery()
    {
        // невидимые зоны нажатия поверх карточек зоопарка (карточки пересоздаются, зоны — нет)
        Label(zooPanel, "Нажмите на животное, чтобы посмотреть его в 3D", 12, Muted, TextAnchor.MiddleLeft, 20, 110, 350, 16);
        for (int i = 0; i < zooHits.Length; i++)
        {
            int idx = i;
            zooHits[i] = TL(zooPanel, 20 + (i % 3) * 120, 128 + (i / 3) * 124, 110, 114, "Hit");
            AddBtn(zooHits[i], () => OpenGallery(idx), 1);
        }

        galleryPanel = TL(content, 0, 0, 390, 700, "GalleryPanel");
        var back = TL(galleryPanel, 20, 82, 140, 36, "Back");
        Label(back, "← Зоопарк", 16, Red, TextAnchor.MiddleLeft, 0, 0, 140, 36);
        AddBtn(back, () => SetTab(1), TabGallery);
        galCount = Label(galleryPanel, "", 14, Muted, TextAnchor.MiddleRight, 230, 82, 140, 36);

        var card = TL(galleryPanel, 20, 124, 350, 380, "View");
        Round(card, Color.white, 28);
        galView = TL(card, 10, 10, 330, 330, "Render");
        galView.gameObject.AddComponent<RawImage>().raycastTarget = false;
        WrapLabel(card, "Крутите пальцем", 12, Muted, TextAnchor.MiddleCenter, 0, 346, 350, 24);

        galName = Label(galleryPanel, "", 21, Ink, TextAnchor.MiddleCenter, 70, 516, 250, 34);
        galInfo = WrapLabel(galleryPanel, "", 13, Muted, TextAnchor.UpperCenter, 70, 552, 250, 40);

        for (int d = -1; d <= 1; d += 2)
        {
            int dir = d;
            var arrow = TL(galleryPanel, d < 0 ? 20 : 316, 516, 54, 54, "Arrow");
            Round(arrow, Color.white, 27);
            Label(arrow, d < 0 ? "‹" : "›", 30, Ink, TextAnchor.MiddleCenter, 0, -3, 54, 54);
            AddBtn(arrow, () => { galIndex = (galIndex + dir + Zoo.Length) % Zoo.Length; GameAudio.Click(); ShowGalleryCreature(); }, TabGallery);
        }
    }

    void OpenGallery(int idx)
    {
        galIndex = Mathf.Clamp(idx, 0, Zoo.Length - 1);
        galYaw = -25f;
        GameAudio.Click();
        SetTab(TabGallery);
    }

    // ============================================================
    //  3D-сцена
    // ============================================================
    void EnsureGalleryScene()
    {
        if (galCam != null) return;
        sphereMesh = sphereMesh != null ? sphereMesh : MakeSphere(24, 16);
        var shader = Resources.Load<Shader>("Shaders/HatchToon");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Color");
        toonMat = new Material(shader) { enableInstancing = true };
        mpb = new MaterialPropertyBlock();

        galRt = new RenderTexture(640, 640, 24) { antiAliasing = 4, name = "GalleryRT" };
        galView.GetComponent<RawImage>().texture = galRt;

        var camGo = new GameObject("GalleryCamera");
        galCam = camGo.AddComponent<Camera>();
        galCam.clearFlags = CameraClearFlags.SolidColor;
        galCam.backgroundColor = Color.white;
        galCam.cullingMask = 1 << GalleryLayer;
        galCam.fieldOfView = 30f;
        galCam.nearClipPlane = 0.1f;
        galCam.farClipPlane = 50f;
        galCam.targetTexture = galRt;
        camGo.transform.position = Stage + new Vector3(0f, 0.55f, -6.4f);
        camGo.transform.LookAt(Stage + new Vector3(0f, 0.1f, 0f));
        if (Camera.main != null) Camera.main.cullingMask &= ~(1 << GalleryLayer);

        galRoot = new GameObject("GalleryStage").transform;
        galRoot.position = Stage;
        galPedestal = Blob(galRoot, new Vector3(0, -1.12f, 0), new Vector3(2.6f, 0.18f, 2.6f), C(255, 224, 184), 0).transform;
        Blob(galPedestal, new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.4f, 0.9f), C(255, 239, 214), 0);
    }

    void ShowGalleryCreature()
    {
        EnsureGalleryScene();
        if (galModel != null) Destroy(galModel.gameObject);
        galEyes.Clear();
        galModel = BuildCreature3D(galIndex, galIndex < zooCount);
        galModel.SetParent(galRoot, false);

        bool open = galIndex < zooCount;
        galName.text = open ? Zoo[galIndex].name : "???";
        galInfo.text = open ? "Открыт в твоём зоопарке" : "Ещё не открыт — вылупится из яйца уровня " + Zoo[galIndex].target;
        galCount.text = (galIndex + 1) + " из " + Zoo.Length;
        galIdle = 0f;
    }

    Transform BuildCreature3D(int idx, bool open)
    {
        var root = new GameObject("Creature3D").transform;
        root.gameObject.layer = GalleryLayer;
        Color body = open ? Zoo[idx].color : C(92, 78, 108);
        Color dark = open ? Color.Lerp(body, Ink, 0.25f) : C(74, 62, 88);
        Color light = open ? Color.Lerp(body, Color.white, 0.55f) : C(110, 96, 126);
        Color ink = open ? Ink : C(60, 50, 72);
        Color pink = open ? C(245, 143, 168) : body;
        Color white = open ? Color.white : light;
        Color gold = open ? C(255, 210, 100) : light;
        Color navy = open ? C(74, 86, 130) : dark, coal = open ? C(66, 58, 80) : dark;
        Color red = open ? C(235, 85, 70) : dark, orange = open ? C(255, 165, 70) : light;
        Color spike = open ? C(140, 100, 70) : dark;

        Color Pick(char k) =>
            k == 'B' ? body : k == 'D' ? dark : k == 'L' ? light : k == 'W' ? white :
            k == 'K' ? ink : k == 'P' ? pink : k == 'Y' ? gold : k == 'N' ? navy :
            k == 'A' ? coal : k == 'R' ? red : k == 'O' ? orange : k == 'S' ? spike : body;

        string spec = Look3D[Mathf.Clamp(Zoo[idx].look, 0, Look3D.Length - 1)];
        bool feet = !spec.TrimEnd().EndsWith("-");
        foreach (var raw in spec.TrimEnd('-', ' ').Split(';'))
        {
            var p = raw.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 8) continue;
            float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
            Blob(root, new Vector3(F(1), F(2), F(3)), new Vector3(F(4), F(5), F(6)), Pick(p[0][0]), F(7));
        }

        // общие части
        Blob(root, new Vector3(0, -0.08f, 0), new Vector3(1.6f, 1.56f, 1.42f), body, 0);                // тело
        Blob(root, new Vector3(0, -0.3f, -0.37f), new Vector3(0.9f, 0.62f, 0.66f), Color.Lerp(body, white, 0.5f), 0);  // животик
        if (feet)
        {
            Blob(root, new Vector3(-0.36f, -0.86f, -0.18f), new Vector3(0.42f, 0.24f, 0.48f), dark, 0);
            Blob(root, new Vector3(0.36f, -0.86f, -0.18f), new Vector3(0.42f, 0.24f, 0.48f), dark, 0);
        }
        for (int s = -1; s <= 1; s += 2)
        {
            var eye = Blob(root, new Vector3(0.3f * s, 0.1f, -0.6f), new Vector3(0.19f, 0.24f, 0.12f), ink, 0).transform;
            if (open) Blob(eye, new Vector3(0.18f, 0.22f, -0.42f), new Vector3(0.38f, 0.32f, 0.3f), Color.white, 0);
            galEyes.Add(eye);
            if (open) Blob(root, new Vector3(0.54f * s, -0.16f, -0.47f), new Vector3(0.25f, 0.14f, 0.08f), pink, 0);   // щёчка
        }
        Blob(root, new Vector3(0, -0.06f, -0.69f), new Vector3(0.12f, 0.06f, 0.05f), ink, 0);              // улыбка
        return root;
    }

    GameObject Blob(Transform parent, Vector3 pos, Vector3 scale, Color col, float rotZ)
    {
        var go = new GameObject("Blob") { layer = GalleryLayer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0, 0, rotZ);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = toonMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mpb.SetColor("_Color", col);
        mr.SetPropertyBlock(mpb);
        return go;
    }

    static Mesh MakeSphere(int lon, int lat)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int y = 0; y <= lat; y++)
        {
            float a = Mathf.PI * y / lat;
            for (int x = 0; x <= lon; x++)
            {
                float b = 2f * Mathf.PI * x / lon;
                v.Add(0.5f * new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)));
            }
        }
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int i = y * (lon + 1) + x;
                t.Add(i); t.Add(i + 1); t.Add(i + lon + 1);
                t.Add(i + 1); t.Add(i + lon + 2); t.Add(i + lon + 1);
            }
        var m = new Mesh { name = "Sphere" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        var n = new List<Vector3>();
        foreach (var p in v) n.Add(p.normalized);
        m.SetNormals(n);
        m.RecalculateBounds();
        return m;
    }

    // ============================================================
    //  Каждый кадр
    // ============================================================
    void UpdateGallery(float dt)
    {
        bool on = tab == TabGallery;
        if (galCam != null) galCam.enabled = on;
        if (!on || galModel == null) { galDragging = false; return; }

        // поворот пальцем
        Vector2 sp = PointerPos();
        if (PointerDown() && RectTransformUtility.RectangleContainsScreenPoint(galView, sp, null)) { galDragging = true; galLast = sp; }
        if (galDragging)
        {
            if (!PointerHeld()) galDragging = false;
            else
            {
                float k = 300f / Mathf.Max(1f, Screen.width);
                galYaw -= (sp.x - galLast.x) * k * 1.2f;
                galPitch = Mathf.Clamp(galPitch + (sp.y - galLast.y) * k * 0.6f, -20f, 25f);
                galLast = sp;
                galIdle = 0f;
            }
        }
        else
        {
            galIdle += dt;
            if (galIdle > 1.5f) galYaw += 22f * dt;           // сам медленно поворачивается
            galPitch = Mathf.Lerp(galPitch, 0f, dt * 2f);
        }

        float time = Time.time;
        galModel.localRotation = Quaternion.Euler(galPitch, galYaw, 0f);
        galModel.localPosition = new Vector3(0f, 0.05f * Mathf.Abs(Mathf.Sin(time * 2.2f)), 0f);   // подпрыгивает
        float squash = 1f + 0.025f * Mathf.Sin(time * 4.4f);
        galModel.localScale = new Vector3(1f / squash, squash, 1f / squash);

        // моргание
        galBlink -= dt;
        float eyeY = galBlink < 0.12f ? 0.15f : 1f;
        if (galBlink < 0f) galBlink = Random.Range(2f, 4.5f);
        foreach (var e in galEyes) if (e != null) e.localScale = new Vector3(0.19f, 0.24f * eyeY, 0.12f);
    }

#if ENABLE_INPUT_SYSTEM
    static bool PointerHeld() => Pointer.current != null && Pointer.current.press.isPressed;
#else
    static bool PointerHeld() => Input.GetMouseButton(0);
#endif
}
