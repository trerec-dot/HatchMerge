// Настройка проекта «Яйца-сюрпризы» в редакторе Unity.
//
// • При первом открытии проекта сама создаёт сцену Assets/Scenes/Main.unity,
//   добавляет её в Build Settings и настраивает Player Settings (название, иконка,
//   портретная ориентация, идентификатор приложения).
// • Повторить вручную: меню «Яйца-сюрпризы → Настроить проект и сцену».
// • Импорт ассетов (спрайты, звуки) настраивается автоматически — см. AssetImportRules ниже.

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string IconPath = "Assets/Resources/Art/app_icon.png";
    const string BundleId = "com.eggstudio.hatchmerge";

    static ProjectSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) Setup(false);
        };
    }

    [MenuItem("Яйца-сюрпризы/Настроить проект и сцену")]
    public static void SetupFromMenu() { Setup(true); }

    static void Setup(bool askToSave)
    {
        if (askToSave && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(255, 243, 224, 255);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0, 0, -10);
        camGo.AddComponent<AudioListener>();

        new GameObject("MergeGame").AddComponent<MergeGame>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        ConfigurePlayer();
        AssetDatabase.SaveAssets();
        Debug.Log("Яйца-сюрпризы: сцена создана (" + ScenePath + "), проект настроен. Нажмите Play.");
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "EggStudio";
        PlayerSettings.productName = "Яйца-сюрпризы";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
        PlayerSettings.SplashScreen.backgroundColor = new Color32(255, 243, 224, 255);

        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
    }
}

// Правила импорта: картинки из Resources/Art — несжатые спрайты без мип-карт,
// музыка — сжатая и потоковая, короткие звуки — моно и распакованные при загрузке.
public class AssetImportRules : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Art/")) return;
        var ti = (TextureImporter)assetImporter;
        bool isIcon = assetPath.EndsWith("app_icon.png");
        ti.textureType = isIcon ? TextureImporterType.Default : TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = !isIcon;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 1024;
    }

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Resources/Audio/")) return;
        var ai = (AudioImporter)assetImporter;
        var s = ai.defaultSampleSettings;
        if (assetPath.Contains("music"))
        {
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
        }
        else
        {
            ai.forceToMono = true;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.ADPCM;
        }
        ai.defaultSampleSettings = s;
    }
}
