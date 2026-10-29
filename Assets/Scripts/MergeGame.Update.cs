// Обновление игры с сервера игроков (без магазина приложений).
// • После подключения к серверу игра спрашивает /api/update. Если номер сборки на сервере больше своего
//   (он записан в Resources/build_info.txt при сборке), показывает окно «Доступно обновление».
// • «Обновить» открывает ссылку на APK: телефон скачивает файл, затем нужно открыть его и нажать «Установить».
//   Прогресс не теряется — обновление ставится поверх старой версии (тот же ключ подписи).
// • «Обязательное» обновление (галочка при публикации) нельзя отложить.
// • В настройках — строка с версией и кнопкой «Проверить».

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
class NetUpdateInfo
{
    public bool ok, force;
    public int versionCode;
    public long size;
    public string versionName, notes, url;
}

public partial class MergeGame
{
    int myVersionCode;
    string myVersionName = "";
    bool updateChecked, updateChecking;
    NetUpdateInfo pendingUpdate;

    RectTransform updOverlay, updLater;
    Text updTitle, updInfo, updNotes, updHint, updMainTxt, setVerTxt;

    void InitVersion()
    {
        myVersionName = Application.version;
        var ta = Resources.Load<TextAsset>("build_info");
        if (ta == null) return;
        foreach (var raw in ta.text.Split('\n'))
        {
            var kv = raw.Trim().Split(new[] { '=' }, 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "code") int.TryParse(kv[1], out myVersionCode);
            if (kv[0] == "name" && kv[1].Length > 0) myVersionName = kv[1];
        }
    }

    string VersionText() => "Версия " + myVersionName + (myVersionCode > 0 ? " (сборка " + myVersionCode + ")" : "");

    // ============================================================
    //  Проверка (вызывается из NetLoop, когда есть связь)
    // ============================================================
    IEnumerator CheckUpdate(bool userAction)
    {
        if (updateChecking) yield break;
        updateChecking = true;
        updateChecked = true;
        if (userAction && setVerTxt != null) setVerTxt.text = "Проверяем…";

        long code = 0;
        NetUpdateInfo info = null;
        yield return Http("GET", netUrl, "/api/update", null, (c, t) => { code = c; info = Parse<NetUpdateInfo>(t); });
        updateChecking = false;
        if (setVerTxt != null) setVerTxt.text = VersionText();

        if (code != 200 || info == null || !info.ok)
        {
            if (userAction) Toast(code == 0 ? "Нет связи с сервером" : "Не удалось проверить обновления");
            yield break;
        }

        // сборки из редактора (номер 0) не предлагают обновиться сами — только по кнопке «Проверить»
        bool newer = info.versionCode > myVersionCode && !string.IsNullOrEmpty(info.url);
        if (newer && (userAction || myVersionCode > 0)) ShowUpdate(info);
        else if (userAction) Toast("У вас последняя версия");
    }

    // ============================================================
    //  Окно обновления
    // ============================================================
    void BuildUpdateUi()
    {
        updOverlay = NewRt("Update", canvasRt);
        updOverlay.anchorMin = Vector2.zero;
        updOverlay.anchorMax = Vector2.one;
        updOverlay.offsetMin = updOverlay.offsetMax = Vector2.zero;
        AddImg(updOverlay, null, new Color(0.23f, 0.16f, 0.29f, 0.55f));

        var box = CC(updOverlay, 0, 10, 340, 420, "Box");
        Round(box, Color.white, 28);
        var mark = TL(box, 140, 24, 60, 60, "Mark");
        AddImg(mark, circleSpr, C(110, 200, 160));
        Label(mark, "↑", 32, Color.white, TextAnchor.MiddleCenter, 0, -2, 60, 60);
        updTitle = Label(box, "Доступно обновление", 22, Ink, TextAnchor.MiddleCenter, 0, 96, 340, 32);
        updInfo = Label(box, "", 14, Muted, TextAnchor.MiddleCenter, 0, 130, 340, 22);
        updNotes = WrapLabel(box, "", 15, Ink, TextAnchor.UpperCenter, 24, 162, 292, 92);

        var main = TL(box, 24, 262, 292, 56, "Do");
        Round(main, Red, 28);
        updMainTxt = Label(main, "Обновить", 19, Color.white, TextAnchor.MiddleCenter, 0, 0, 292, 56);
        updLater = TL(box, 24, 326, 292, 44, "Later");
        Label(updLater, "Позже", 16, Muted, TextAnchor.MiddleCenter, 0, 0, 292, 44);
        updHint = WrapLabel(box, "", 12, Muted, TextAnchor.UpperCenter, 20, 372, 300, 44);

        // кнопки окна проверяются раньше всех остальных, а «заглушка» на весь экран
        // забирает нажатия, чтобы под окном ничего не срабатывало
        btns.Insert(0, new Btn { rt = main, act = DoUpdate, tab = -1 });
        btns.Insert(1, new Btn { rt = updLater, act = () => { GameAudio.Click(); updOverlay.gameObject.SetActive(false); }, tab = -1 });
        btns.Insert(2, new Btn { rt = updOverlay, act = () => { }, tab = -1 });
        updOverlay.gameObject.SetActive(false);
    }

    void ShowUpdate(NetUpdateInfo info)
    {
        pendingUpdate = info;
        updOverlay.SetAsLastSibling();
        updOverlay.gameObject.SetActive(true);
        updTitle.text = info.force ? "Нужно обновление" : "Доступно обновление";
        string size = info.size > 0 ? " · " + (info.size / 1024f / 1024f).ToString("0.0") + " МБ" : "";
        updInfo.text = "Версия " + (string.IsNullOrEmpty(info.versionName) ? info.versionCode.ToString() : info.versionName) + size;
        updNotes.text = string.IsNullOrEmpty(info.notes) ? "Новая версия игры." : info.notes;
        updMainTxt.text = "Обновить";
        updLater.gameObject.SetActive(!info.force);
        updHint.text = info.force ? "Эта версия игры устарела — без обновления игра может работать неправильно." : "";
        CancelDrag();
        GameAudio.Buy();
    }

    void DoUpdate()
    {
        if (pendingUpdate == null) return;
        GameAudio.Click();
        Save(); PlayerPrefs.Save();
        string url = pendingUpdate.url.StartsWith("http") ? pendingUpdate.url : netUrl.TrimEnd('/') + pendingUpdate.url;
        Application.OpenURL(url);
        updMainTxt.text = "Скачать ещё раз";
        updHint.text = "Загрузка началась. Когда файл скачается, откройте его (в уведомлениях или в «Загрузках») и нажмите «Установить». " +
                       "Если Android спросит — разрешите установку из этого источника.";
    }

    // строка «Версия» в настройках
    void BuildVersionRow(RectTransform parent, float y)
    {
        var card = TL(parent, 20, y, 350, 64, "Version");
        Round(card, Color.white, 22);
        setVerTxt = Label(card, VersionText(), 14, Ink, TextAnchor.MiddleLeft, 16, 0, 230, 64);
        var b = TL(card, 252, 14, 86, 36, "Check");
        Round(b, C(238, 227, 247), 18);
        Label(b, "Проверить", 13, Ink, TextAnchor.MiddleCenter, 0, 0, 86, 36);
        AddBtn(b, () =>
        {
            GameAudio.Click();
            if (netState != NetState.Online) { Toast("Нет связи с сервером"); KickNet(); return; }
            StartCoroutine(CheckUpdate(true));
        }, 3);
    }
}
