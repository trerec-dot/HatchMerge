// Тестовые команды для проверки новых механик в редакторе (работают только в режиме Play).
using UnityEditor;

static class DebugMenu
{
    [MenuItem("Яйца-сюрпризы/Тест/Особые яйца и сундук на поле")]
    static void Specials() => MergeGame.DebugSpecials();

    [MenuItem("Яйца-сюрпризы/Тест/Сундук готов к открытию")]
    static void ChestReady() => MergeGame.DebugChestReady();

    [MenuItem("Яйца-сюрпризы/Тест/Прошли сутки (награды, голодные животные)")]
    static void NextDay() => MergeGame.DebugNextDay();

    [MenuItem("Яйца-сюрпризы/Тест/Особые яйца и сундук на поле", true)]
    [MenuItem("Яйца-сюрпризы/Тест/Сундук готов к открытию", true)]
    [MenuItem("Яйца-сюрпризы/Тест/Прошли сутки (награды, голодные животные)", true)]
    static bool Playing() => EditorApplication.isPlaying;
}
