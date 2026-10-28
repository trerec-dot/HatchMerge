// Сборка тестового APK для Android одной командой:
// меню «Яйца-сюрпризы → Собрать тестовый APK (Android)».
// Готовый файл: папка Builds в корне проекта, HatchMerge-test.apk.
//
// Нужен модуль Android Build Support (вместе с OpenJDK и Android SDK & NDK Tools),
// который ставится в Unity Hub: Installs → ⚙ у версии Unity → Add modules.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildTools
{
    const string ApkPath = "Builds/HatchMerge-test.apk";

    [MenuItem("Яйца-сюрпризы/Собрать тестовый APK (Android)")]
    public static void BuildTestApk()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        {
            EditorUtility.DisplayDialog("Нет модуля Android",
                "Для сборки APK установите в Unity Hub модуль «Android Build Support» " +
                "вместе с «OpenJDK» и «Android SDK & NDK Tools» для этой версии Unity, затем перезапустите Unity.",
                "Понятно");
            return;
        }

        var report = Build();
        if (report.summary.result == BuildResult.Succeeded)
        {
            float mb = new FileInfo(ApkPath).Length / 1024f / 1024f;
            EditorUtility.RevealInFinder(ApkPath);
            EditorUtility.DisplayDialog("APK собран",
                $"Файл: {Path.GetFullPath(ApkPath)}\nРазмер: {mb:0.0} МБ\n\n" +
                "Скопируйте его на телефон и откройте. Если Android спросит — разрешите установку из этого источника.",
                "Отлично");
        }
        else
        {
            EditorUtility.DisplayDialog("Сборка не удалась",
                $"Результат: {report.summary.result}, ошибок: {report.summary.totalErrors}.\nПодробности — в окне Console.",
                "OK");
        }
    }

    // для запуска из командной строки: Unity.exe -batchmode -quit -projectPath ... -executeMethod BuildTools.BuildTestApkCli
    public static void BuildTestApkCli()
    {
        var report = Build();
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    static BuildReport Build()
    {
        // 64-битная сборка IL2CPP — запускается на всех современных телефонах
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        EditorUserBuildSettings.buildAppBundle = false;      // нужен .apk, а не .aab

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/Main.unity" };

        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[HatchMerge] Сборка APK: {report.summary.result}, {report.summary.totalSize / 1024 / 1024} МБ, " +
                  $"ошибок {report.summary.totalErrors} → {Path.GetFullPath(ApkPath)}");
        return report;
    }
}
