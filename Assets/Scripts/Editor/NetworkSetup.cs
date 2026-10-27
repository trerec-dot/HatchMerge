// Разрешает игре ходить на сервер игроков по обычному http (сервер на ПК работает без https)
// и включает доступ в интернет на Android. Срабатывает автоматически при открытии проекта.

using UnityEditor;

[InitializeOnLoad]
static class HatchMergeNetworkSetup
{
    static HatchMergeNetworkSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            if (!PlayerSettings.Android.forceInternetPermission)
                PlayerSettings.Android.forceInternetPermission = true;
        };
    }
}
