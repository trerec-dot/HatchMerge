// Сборка и публикация Android-версии.
//
// • «Яйца-сюрпризы → Собрать тестовый APK (Android)» — собрать APK в папку Builds.
// • «Яйца-сюрпризы → Опубликовать обновление…» — собрать APK и загрузить на сервер игроков;
//   игры на телефонах увидят новую версию и предложат обновиться.
//
// Каждая сборка увеличивает номер сборки (versionCode) и записывает его в Resources/build_info.txt —
// по нему игра понимает, что на сервере версия новее.
//
// Подпись: при первой сборке создаётся постоянный ключ в папке Keystore рядом с проектом
// (в Git не попадает). Обновления ставятся поверх старой версии, только если ключ тот же —
// СОХРАНИТЕ копию папки Keystore. Потеряете ключ — игрокам придётся удалить игру и поставить заново.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

public static class BuildTools
{
    public const string ApkPath = "Builds/HatchMerge-test.apk";
    const string KeyDir = "Keystore";
    const string KeyFile = "Keystore/hatchmerge.keystore";
    const string KeyInfo = "Keystore/keystore-password.txt";
    const string KeyAlias = "hatchmerge";
    const string BuildInfo = "Assets/Resources/build_info.txt";

    [MenuItem("Яйца-сюрпризы/Собрать тестовый APK (Android)", priority = 20)]
    public static void BuildTestApk()
    {
        if (!AndroidReady()) return;
        var report = Build();
        if (report.summary.result == BuildResult.Succeeded)
        {
            float mb = new FileInfo(ApkPath).Length / 1024f / 1024f;
            EditorUtility.RevealInFinder(ApkPath);
            EditorUtility.DisplayDialog("APK собран",
                $"Версия {PlayerSettings.bundleVersion}, сборка {PlayerSettings.Android.bundleVersionCode}\n" +
                $"Файл: {Path.GetFullPath(ApkPath)}\nРазмер: {mb:0.0} МБ\n\n" +
                "Скопируйте его на телефон и откройте. Если Android спросит — разрешите установку из этого источника.",
                "Отлично");
        }
        else ShowFailed(report);
    }

    [MenuItem("Яйца-сюрпризы/Опубликовать обновление…", priority = 21)]
    public static void OpenPublish() => PublishWindow.Open();

    // для запуска из командной строки: Unity.exe -batchmode -quit -projectPath ... -executeMethod BuildTools.BuildTestApkCli
    public static void BuildTestApkCli()
    {
        var report = Build();
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    public static bool AndroidReady()
    {
        if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)) return true;
        EditorUtility.DisplayDialog("Нет модуля Android",
            "Для сборки APK установите в Unity Hub модуль «Android Build Support» " +
            "вместе с «OpenJDK» и «Android SDK & NDK Tools» для этой версии Unity, затем перезапустите Unity.",
            "Понятно");
        return false;
    }

    public static void ShowFailed(BuildReport report)
    {
        EditorUtility.DisplayDialog("Сборка не удалась",
            $"Результат: {report.summary.result}, ошибок: {report.summary.totalErrors}.\nПодробности — в окне Console.", "OK");
    }

    public static BuildReport Build()
    {
        // 64-битная сборка IL2CPP — запускается на всех современных телефонах
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        EditorUserBuildSettings.buildAppBundle = false;      // нужен .apk, а не .aab

        SetupSigning();

        // новый номер сборки и файл с ним для игры
        PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode + 1);
        WriteBuildInfo();

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/Main.unity" };

        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        });
        AssetDatabase.SaveAssets();                           // сохранить новый номер сборки в настройках проекта
        Debug.Log($"[HatchMerge] Сборка APK {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}): " +
                  $"{report.summary.result}, ошибок {report.summary.totalErrors} → {Path.GetFullPath(ApkPath)}");
        return report;
    }

    static void WriteBuildInfo()
    {
        File.WriteAllText(BuildInfo,
            $"code={PlayerSettings.Android.bundleVersionCode}\nname={PlayerSettings.bundleVersion}\n");
        AssetDatabase.ImportAsset(BuildInfo, ImportAssetOptions.ForceUpdate);
    }

    // ---------- постоянный ключ подписи ----------
    static void SetupSigning()
    {
        string pass = File.Exists(KeyInfo) ? File.ReadAllLines(KeyInfo).FirstOrDefault()?.Trim() : null;
        if (!File.Exists(KeyFile) || string.IsNullOrEmpty(pass))
        {
            if (File.Exists(KeyFile))
                throw new BuildFailedException($"Есть {KeyFile}, но нет пароля в {KeyInfo}. Верните файл с паролем из резервной копии.");
            pass = NewPassword();
            CreateKeystore(pass);
            File.WriteAllText(KeyInfo, pass + "\n\nПароль ключа подписи игры. Храните вместе с hatchmerge.keystore и сделайте копию этой папки.\n");
            EditorUtility.DisplayDialog("Создан ключ подписи",
                $"Создан постоянный ключ подписи в папке {Path.GetFullPath(KeyDir)}.\n\n" +
                "Сделайте копию этой папки (например, на флешку или в облако). Без неё обновления не встанут поверх установленной игры.",
                "Понятно");
        }
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = Path.GetFullPath(KeyFile);
        PlayerSettings.Android.keystorePass = pass;
        PlayerSettings.Android.keyaliasName = KeyAlias;
        PlayerSettings.Android.keyaliasPass = pass;
    }

    static string NewPassword()
    {
        var bytes = new byte[18];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
    }

    static void CreateKeystore(string pass)
    {
        Directory.CreateDirectory(KeyDir);
        string keytool = FindKeytool();
        var psi = new ProcessStartInfo(keytool,
            $"-genkeypair -keystore \"{Path.GetFullPath(KeyFile)}\" -alias {KeyAlias} -keyalg RSA -keysize 2048 " +
            $"-validity 10000 -storepass {pass} -keypass {pass} -dname \"CN=HatchMerge, O=EggStudio, C=RU\"")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (var p = Process.Start(psi))
        {
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            if (p.ExitCode != 0 || !File.Exists(KeyFile))
                throw new BuildFailedException("Не удалось создать ключ подписи: " + err);
        }
    }

    static string FindKeytool()
    {
        string exe = Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool";
        // путь к JDK из настроек Unity (External Tools); тип ищем по имени, чтобы скрипт компилировался и без модуля Android
        var t = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
        string jdk = t?.GetProperty("jdkRootPath")?.GetValue(null) as string;
        foreach (var root in new[] { jdk, Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/OpenJDK") })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string path = Path.Combine(root, "bin", exe);
            if (File.Exists(path)) return path;
        }
        throw new BuildFailedException("Не найден keytool. Проверьте, что в Unity Hub установлен модуль OpenJDK.");
    }

    // ---------- загрузка на сервер ----------
    public static string DefaultServerUrl()
    {
        var path = "Assets/Resources/server_url.txt";
        if (!File.Exists(path)) return "";
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("https://") || line.StartsWith("http://")) return line.TrimEnd('/');
        }
        return "";
    }

    public static UnityWebRequest UploadRequest(string server, string adminPassword, string notes, bool force)
    {
        string url = server.TrimEnd('/') + "/api/admin/release" +
                     $"?versionCode={PlayerSettings.Android.bundleVersionCode}" +
                     $"&versionName={UnityWebRequest.EscapeURL(PlayerSettings.bundleVersion)}" +
                     $"&notes={UnityWebRequest.EscapeURL(notes ?? "")}&force={(force ? 1 : 0)}";
        var req = new UnityWebRequest(url, "POST")
        {
            uploadHandler = new UploadHandlerFile(Path.GetFullPath(ApkPath)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 1800,
        };
        req.SetRequestHeader("Authorization", "Bearer " + adminPassword);
        req.SetRequestHeader("Content-Type", "application/octet-stream");
        return req;
    }
}

// Окно «Опубликовать обновление»
public class PublishWindow : EditorWindow
{
    string server, password, notes = "", status = "";
    bool force, remember = true;
    UnityWebRequest upload;

    public static void Open()
    {
        var w = GetWindow<PublishWindow>(true, "Опубликовать обновление");
        w.minSize = new Vector2(460, 330);
        w.server = EditorPrefs.GetString("HatchMerge.PublishServer", BuildTools.DefaultServerUrl());
        w.password = EditorPrefs.GetString("HatchMerge.AdminPassword", "");
        w.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Соберёт APK и загрузит его на сервер игроков.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField($"Версия {PlayerSettings.bundleVersion}, следующая сборка №{PlayerSettings.Android.bundleVersionCode + 1}",
            EditorStyles.miniLabel);
        EditorGUILayout.Space();
        server = EditorGUILayout.TextField("Сервер", server);
        password = EditorGUILayout.PasswordField("Пароль администратора", password);
        remember = EditorGUILayout.Toggle("Запомнить на этом ПК", remember);
        EditorGUILayout.LabelField("Что нового (увидят игроки):");
        notes = EditorGUILayout.TextArea(notes, GUILayout.MinHeight(54));
        force = EditorGUILayout.Toggle(new GUIContent("Обязательное", "Без кнопки «Позже» — например, если старая версия не работает с сервером"), force);
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(upload != null || string.IsNullOrEmpty(server) || string.IsNullOrEmpty(password)))
            if (GUILayout.Button(upload != null ? "Загружаем…" : "Собрать и опубликовать", GUILayout.Height(34)))
                Publish();

        if (upload != null)
        {
            var r = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(r, upload.uploadProgress, $"Загрузка {upload.uploadProgress * 100:0}%");
            Repaint();
        }
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
    }

    void Publish()
    {
        if (remember)
        {
            EditorPrefs.SetString("HatchMerge.PublishServer", server);
            EditorPrefs.SetString("HatchMerge.AdminPassword", password);
        }
        else EditorPrefs.DeleteKey("HatchMerge.AdminPassword");

        if (!BuildTools.AndroidReady()) return;
        status = "Собираем APK…";
        var report = BuildTools.Build();
        if (report.summary.result != BuildResult.Succeeded) { status = "Сборка не удалась — см. Console."; BuildTools.ShowFailed(report); return; }

        status = "Загружаем на сервер…";
        upload = BuildTools.UploadRequest(server, password, notes, force);
        var op = upload.SendWebRequest();
        op.completed += _ =>
        {
            string body = upload.downloadHandler?.text ?? "";
            if (upload.result == UnityWebRequest.Result.Success)
            {
                status = $"Готово! Опубликована версия {PlayerSettings.bundleVersion} (сборка {PlayerSettings.Android.bundleVersionCode}). " +
                         "Игроки увидят предложение обновиться при следующем запуске игры.";
                Debug.Log("[HatchMerge] " + status);
            }
            else
            {
                status = upload.responseCode == 403 ? "Неверный пароль администратора (или на сервере он не задан)."
                    : $"Ошибка загрузки ({upload.responseCode}): {upload.error}\n{body}";
                Debug.LogError("[HatchMerge] " + status);
            }
            upload.Dispose();
            upload = null;
            Repaint();
        };
    }
}
