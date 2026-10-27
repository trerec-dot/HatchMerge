// Учётная запись: вход по почте и паролю, облачное сохранение всего прогресса
// и синхронизация между устройствами.
//
// • Гость играет без регистрации. «Создать аккаунт» привязывает почту к текущему игроку —
//   его прогресс сразу сохраняется на сервере.
// • «Войти» на другом устройстве загружает прогресс из аккаунта (прогресс гостя на нём заменяется).
// • Пока игрок в аккаунте, изменения отправляются на сервер не чаще раза в 6 секунд.
//   У каждого сохранения есть номер версии: если другое устройство успело сохранить новее,
//   сервер отвечает 409 и игра загружает более новую версию.
// • Текст вводится без EventSystem: на телефоне открывается системная клавиатура,
//   на компьютере буквы приходят через OnGUI (работают обычная клавиатура и Ctrl+V).

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
class GameSave
{
    public int v, score, gems, zoo, energy;
    public bool music, sfx;
    public int[] grid;
}

[Serializable] class NetAccountReq { public string id, token, email, password; }
[Serializable] class NetLoginReq { public string email, password; }
[Serializable] class NetAuthReq { public string id, token; }
[Serializable] class NetSaveReq { public string id, token, save; public long baseVer; }

[Serializable]
class NetAccountResp
{
    public bool ok;
    public string error, email, id, token, save, reason;
    public long saveVer;
    public int nickA, nickB;
}

public partial class MergeGame
{
    const int TabAccount = 6;
    const float UploadEvery = 6f;

    // ---------- состояние аккаунта ----------
    string accEmail = "";
    long accVer;                 // версия сохранения, с которой совпадает этот телефон
    bool accDirty;               // есть изменения, ещё не отправленные на сервер
    bool accLoaded;              // в этом запуске уже сверились с сервером
    int accStamp;                // счётчик изменений (чтобы не потерять ход, сделанный во время отправки)
    float accLastUpload = -999f, logoutArm;
    bool accBusy, accRegisterMode;

    bool HasAccount => !string.IsNullOrEmpty(accEmail) && !string.IsNullOrEmpty(netId);

    // ---------- интерфейс ----------
    class TextField
    {
        public RectTransform rt;
        public Text txt;
        public Image bg;
        public string value = "", placeholder;
        public bool secret, reveal;
        public int max = 64;
        public TouchScreenKeyboardType kb;
    }

    RectTransform accountPanel, accGuest, accUser;
    readonly Image[] accModeImg = new Image[2];
    readonly Text[] accModeTxt = new Text[2];
    Image accMainImg;
    Text accMainTxt, accMsg, accHint, accShowTxt, accEmailTxt, accIdTxt, accSyncTxt, accLogoutTxt, accUserMsg, setAccTxt;
    TextField accEmailField, accPassField, focused;
    TouchScreenKeyboard tsk;

    void InitAccount()
    {
        accEmail = PlayerPrefs.GetString("acc_email", "");
        long.TryParse(PlayerPrefs.GetString("acc_ver", "0"), out accVer);
        accDirty = PlayerPrefs.GetInt("acc_dirty", 0) == 1;
        if (string.IsNullOrEmpty(netId)) ForgetAccountLocal();
    }

    // ============================================================
    //  Сохранение прогресса
    // ============================================================
    GameSave MakeSave()
    {
        var g = new int[Cols * Rows];
        for (int i = 0; i < g.Length; i++)
        {
            var e = grid[i % Cols, i / Cols];
            g[i] = e != null ? e.level : 0;
        }
        return new GameSave
        {
            v = 1, score = score, gems = gems, zoo = zooCount, energy = energy,
            music = GameAudio.MusicOn, sfx = GameAudio.SfxOn, grid = g,
        };
    }

    // заменить текущий прогресс сохранением (с сервера)
    bool ApplySave(string json)
    {
        var s = Parse<GameSave>(json);
        if (s == null) return false;

        CancelDrag();
        ClearHint();
        score = Mathf.Max(0, s.score);
        gems = Mathf.Max(0, s.gems);
        zooCount = Mathf.Clamp(s.zoo, 0, Zoo.Length);
        energy = Mathf.Clamp(s.energy, 0, MaxGrantedEnergy);
        GameAudio.SetMusic(s.music);
        GameAudio.SetSfx(s.sfx);

        foreach (var e in grid) if (e != null) Destroy(e.gameObject);
        Array.Clear(grid, 0, grid.Length);
        bool any = false;
        if (s.grid != null && s.grid.Length == Cols * Rows)
            for (int i = 0; i < s.grid.Length; i++)
                if (s.grid[i] >= 1 && s.grid[i] <= MaxLevel) { Spawn(i % Cols, i / Cols, s.grid[i]); any = true; }
        if (!any) Seed();

        RefreshAll();
        ApplyToggles();
        SaveLocal();
        if (tab == TabStart) RefreshStartMenu();
        if (tab == 1) RefreshZooPanel();
        if (tab == TabPlayers) RefreshMyCard();
        return true;
    }

    void CancelDrag()
    {
        if (dragged != null)
        {
            dragged.dragging = false;
            dragged.transform.SetParent(eggLayer, false);
            dragged.home = CellCenter(dragFrom.x, dragFrom.y);
            dragged = null;
        }
        btnPressed = false;
        if (btnRt != null) btnRt.localScale = Vector3.one;
    }

    void MarkSaveDirty()
    {
        if (!HasAccount) return;
        accStamp++;
        if (!accDirty) { accDirty = true; PlayerPrefs.SetInt("acc_dirty", 1); }
    }

    void ClearSaveDirty()
    {
        accDirty = false;
        PlayerPrefs.SetInt("acc_dirty", 0);
    }

    void SetAccVer(long v)
    {
        accVer = v;
        PlayerPrefs.SetString("acc_ver", v.ToString());
    }

    void ForgetAccountLocal()
    {
        accEmail = "";
        accLoaded = false;
        SetAccVer(0);
        ClearSaveDirty();
        PlayerPrefs.DeleteKey("acc_email");
    }

    // ============================================================
    //  Синхронизация (вызывается из NetLoop, когда есть связь)
    // ============================================================
    IEnumerator SyncSave()
    {
        if (!accLoaded)
        {
            NetAccountResp r = null;
            yield return Http("POST", netUrl, "/api/load", JsonUtility.ToJson(new NetAuthReq { id = netId, token = netToken }),
                (c, t) => { if (c == 200) r = Parse<NetAccountResp>(t); });
            if (r == null) yield break;
            accLoaded = true;

            if (string.IsNullOrEmpty(r.email)) { ForgetAccountLocal(); RefreshAccount(); yield break; }   // аккаунт удалён на сервере
            if (r.saveVer > accVer && !string.IsNullOrEmpty(r.save))
            {
                // на сервере новее — например, играли на другом устройстве
                if (ApplySave(r.save)) { SetAccVer(r.saveVer); ClearSaveDirty(); Toast("Прогресс загружен с сервера"); }
            }
            else if (r.saveVer < accVer || string.IsNullOrEmpty(r.save))
            {
                SetAccVer(r.saveVer);        // на сервере старее (например, восстановлена копия) — отправим своё
                MarkSaveDirty();
            }
            RefreshAccount();
        }

        if (accDirty && Time.unscaledTime - accLastUpload >= UploadEvery) yield return UploadSave();
    }

    IEnumerator UploadSave()
    {
        accLastUpload = Time.unscaledTime;
        int stamp = accStamp;
        var req = new NetSaveReq { id = netId, token = netToken, save = JsonUtility.ToJson(MakeSave()), baseVer = accVer };
        long code = 0;
        NetAccountResp r = null;
        yield return Http("POST", netUrl, "/api/save", JsonUtility.ToJson(req), (c, t) => { code = c; r = Parse<NetAccountResp>(t); });

        if (code == 200 && r != null)
        {
            SetAccVer(r.saveVer);
            if (stamp == accStamp) ClearSaveDirty();     // во время отправки ходов не было
        }
        else if (code == 409 && r != null && !string.IsNullOrEmpty(r.save))
        {
            // другое устройство сохранило раньше нас — берём его версию
            if (ApplySave(r.save)) Toast("Загружен прогресс с другого устройства");
            SetAccVer(r.saveVer);
            ClearSaveDirty();
        }
        RefreshAccount();
    }

    // при сворачивании/выходе — отправить несохранённое без ожидания ответа
    void AccountGoodbye()
    {
        if (!HasAccount || !accDirty || netState != NetState.Online) return;
        try
        {
            var body = JsonUtility.ToJson(new NetSaveReq
            { id = netId, token = netToken, save = JsonUtility.ToJson(MakeSave()), baseVer = accVer });
            var req = MakeRequest("POST", netUrl + "/api/save", body, 3);
            req.SendWebRequest().completed += _ => req.Dispose();
        }
        catch (Exception) { }
    }

    // ============================================================
    //  Вход, регистрация, выход
    // ============================================================
    void AccSubmit()
    {
        if (accBusy) return;
        Blur();
        string email = accEmailField.value.Trim(), pass = accPassField.value;
        if (email.IndexOf('@') < 1 || email.IndexOf('.') < 0) { GameAudio.Deny(); AccMessage("Проверьте адрес почты", true); return; }
        if (pass.Length < 6) { GameAudio.Deny(); AccMessage("Пароль — не короче 6 символов", true); return; }
        if (netState != NetState.Online) { GameAudio.Deny(); AccMessage("Нет связи с сервером", true); KickNet(); return; }
        GameAudio.Click();
        StartCoroutine(accRegisterMode ? DoRegister(email, pass) : DoLogin(email, pass));
    }

    IEnumerator DoLogin(string email, string pass)
    {
        accBusy = true;
        AccMessage("Входим…", false);
        long code = 0;
        NetAccountResp r = null;
        yield return Http("POST", netUrl, "/api/account/login", JsonUtility.ToJson(new NetLoginReq { email = email, password = pass }),
            (c, t) => { code = c; r = Parse<NetAccountResp>(t); }, 15);
        accBusy = false;

        if (code != 200 || r == null || !r.ok || string.IsNullOrEmpty(r.id)) { GameAudio.Deny(); AccMessage(AccError(code, r), true); yield break; }

        netId = r.id; netToken = r.token;
        PlayerPrefs.SetString("net_id", netId);
        PlayerPrefs.SetString("net_token", netToken);
        nickA = Mathf.Clamp(r.nickA, 0, NickA.Length - 1);
        nickB = Mathf.Clamp(r.nickB, 0, NickB.Length - 1);
        PlayerPrefs.SetInt("net_nickA", nickA);
        PlayerPrefs.SetInt("net_nickB", nickB);
        accEmail = r.email;
        PlayerPrefs.SetString("acc_email", accEmail);

        if (!string.IsNullOrEmpty(r.save) && ApplySave(r.save)) { SetAccVer(r.saveVer); ClearSaveDirty(); }
        else { SetAccVer(r.saveVer); MarkSaveDirty(); }
        accLoaded = true;
        PlayerPrefs.Save();

        accPassField.value = "";
        GameAudio.Buy();
        KickNet();
        RefreshAccount();
        AccMessage("Вы вошли. Прогресс загружен из аккаунта", false, true);
    }

    IEnumerator DoRegister(string email, string pass)
    {
        accBusy = true;
        AccMessage("Создаём аккаунт…", false);
        long code = 0;
        NetAccountResp r = null;
        var req = new NetAccountReq { id = netId, token = netToken, email = email, password = pass };
        yield return Http("POST", netUrl, "/api/account/register", JsonUtility.ToJson(req),
            (c, t) => { code = c; r = Parse<NetAccountResp>(t); }, 15);
        accBusy = false;

        if (code != 200 || r == null || !r.ok) { GameAudio.Deny(); AccMessage(AccError(code, r), true); yield break; }

        accEmail = r.email;
        PlayerPrefs.SetString("acc_email", accEmail);
        SetAccVer(0);
        accLoaded = false;          // NetLoop сверится с сервером и сразу отправит текущий прогресс
        MarkSaveDirty();
        accLastUpload = -999f;
        PlayerPrefs.Save();

        accPassField.value = "";
        GameAudio.Buy();
        KickNet();
        RefreshAccount();
        AccMessage("Аккаунт создан, прогресс сохраняется на сервере", false, true);
    }

    static string AccError(long code, NetAccountResp r)
    {
        if (code == 0) return "Нет связи с сервером";
        switch (r != null ? r.error : null)
        {
            case "bad_email": return "Проверьте адрес почты";
            case "weak_password": return "Пароль — не короче 6 символов";
            case "email_taken": return "Эта почта уже зарегистрирована — нажмите «Вход»";
            case "already_linked": return "Этот игрок уже привязан к другой почте";
            case "bad_login": return "Неверная почта или пароль";
            case "too_many_requests": return "Слишком много попыток. Подождите 15 минут";
            case "banned": return "Этот аккаунт заблокирован" + (string.IsNullOrEmpty(r.reason) ? "" : ": " + r.reason);
            case "not_found": return "На сервере старая версия — обновите сервер (deploy_to_vps.bat)";
            default: return "Ошибка сервера (" + code + ")";
        }
    }

    void ArmLogout()
    {
        if (logoutArm > 0f) { logoutArm = 0f; StartCoroutine(Logout()); return; }
        logoutArm = 3f;
        accLogoutTxt.text = "Ещё раз — прогресс на устройстве сбросится";
        GameAudio.Click();
    }

    IEnumerator Logout()
    {
        accLogoutTxt.text = "Выйти из аккаунта";
        if (accDirty && netState == NetState.Online) yield return UploadSave();   // не потерять последние ходы

        ForgetAccountLocal();
        netId = netToken = "";
        PlayerPrefs.DeleteKey("net_id");
        PlayerPrefs.DeleteKey("net_token");
        SetNetState(NetState.Connecting);    // NetLoop зарегистрирует нового гостя
        ResetGame();                          // на устройстве начинается новая игра
        SetTab(TabAccount);
        AccMessage("Вы вышли из аккаунта", false);
    }

    // ============================================================
    //  Экран «Аккаунт»
    // ============================================================
    static Text WrapLabel(Transform p, string s, int size, Color c, TextAnchor a, float x, float y, float w, float h)
    {
        var t = Label(p, s, size, c, a, x, y, w, h);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        return t;
    }

    void BuildAccount()
    {
        accountPanel = TL(content, 0, 0, 390, 700, "AccountPanel");
        Label(accountPanel, "Аккаунт", 22, Ink, TextAnchor.MiddleLeft, 20, 82, 260, 36);
        WrapLabel(accountPanel, "С аккаунтом прогресс хранится на сервере, и можно играть на нескольких устройствах",
            13, Muted, TextAnchor.UpperLeft, 20, 118, 350, 40);

        // --- гость: вход или регистрация ---
        accGuest = TL(accountPanel, 0, 0, 390, 700, "Guest");
        string[] modes = { "Вход", "Регистрация" };
        for (int i = 0; i < 2; i++)
        {
            int m = i;
            var pill = TL(accGuest, 20 + i * 179, 166, 171, 40, "Mode");
            accModeImg[i] = Round(pill, Color.white, 20);
            accModeTxt[i] = Label(pill, modes[i], 15, Ink, TextAnchor.MiddleCenter, 0, 0, 171, 40);
            AddBtn(pill, () => { accRegisterMode = m == 1; GameAudio.Click(); AccMessage("", false); RefreshAccount(); }, TabAccount);
        }

        accEmailField = MakeField(accGuest, 218, "Почта", false, TouchScreenKeyboardType.EmailAddress, 254);
        accPassField = MakeField(accGuest, 280, "Пароль", true, TouchScreenKeyboardType.Default, 128);

        // кнопка «Показать» внутри поля пароля — регистрируем раньше самого поля, чтобы нажатие попало в неё
        var show = TL(accPassField.rt, 262, 8, 80, 36, "Show");
        Round(show, C(238, 227, 247), 18);
        accShowTxt = Label(show, "Показать", 12, Ink, TextAnchor.MiddleCenter, 0, 0, 80, 36);
        AddBtn(show, () => { accPassField.reveal = !accPassField.reveal; GameAudio.Click(); RefreshFields(); }, TabAccount);
        AddBtn(accEmailField.rt, () => FocusField(accEmailField), TabAccount);
        AddBtn(accPassField.rt, () => FocusField(accPassField), TabAccount);

        var main = TL(accGuest, 20, 344, 350, 56, "Submit");
        accMainImg = Round(main, Red, 28);
        accMainTxt = Label(main, "Войти", 19, Color.white, TextAnchor.MiddleCenter, 0, 0, 350, 56);
        AddBtn(main, AccSubmit, TabAccount);

        accMsg = WrapLabel(accGuest, "", 14, Red, TextAnchor.UpperCenter, 20, 410, 350, 40);
        accHint = WrapLabel(accGuest, "", 12, Muted, TextAnchor.UpperCenter, 20, 458, 350, 60);

        // --- вошедший игрок ---
        accUser = TL(accountPanel, 0, 0, 390, 700, "User");
        var card = TL(accUser, 20, 166, 350, 96, "Card");
        Round(card, Color.white, 24);
        Label(card, "Вы вошли как", 13, Muted, TextAnchor.MiddleLeft, 18, 12, 314, 22);
        accEmailTxt = Label(card, "", 17, Ink, TextAnchor.MiddleLeft, 18, 34, 314, 28);
        accIdTxt = Label(card, "", 13, Muted, TextAnchor.MiddleLeft, 18, 62, 314, 22);

        accSyncTxt = WrapLabel(accUser, "", 14, Ink, TextAnchor.MiddleCenter, 20, 274, 350, 40);

        var saveBtn = TL(accUser, 20, 324, 350, 56, "SaveNow");
        Round(saveBtn, Color.white, 28);
        Label(saveBtn, "Сохранить сейчас", 17, Ink, TextAnchor.MiddleCenter, 0, 0, 350, 56);
        AddBtn(saveBtn, () =>
        {
            GameAudio.Click();
            MarkSaveDirty();
            accLastUpload = -999f;
            KickNet();
            RefreshAccount();
        }, TabAccount);

        var outBtn = TL(accUser, 20, 392, 350, 56, "Logout");
        Round(outBtn, Color.white, 28);
        accLogoutTxt = Label(outBtn, "Выйти из аккаунта", 15, Red, TextAnchor.MiddleCenter, 0, 0, 350, 56);
        AddBtn(outBtn, ArmLogout, TabAccount);

        accUserMsg = WrapLabel(accUser, "", 14, Muted, TextAnchor.UpperCenter, 20, 460, 350, 40);
    }

    TextField MakeField(RectTransform parent, float y, string placeholder, bool secret, TouchScreenKeyboardType kb, int max)
    {
        var f = new TextField { placeholder = placeholder, secret = secret, kb = kb, max = max };
        f.rt = TL(parent, 20, y, 350, 52, "Field");
        f.bg = Round(f.rt, Color.white, 20);
        f.txt = Label(f.rt, "", 17, Ink, TextAnchor.MiddleLeft, 18, 0, secret ? 236 : 314, 52);
        f.txt.horizontalOverflow = HorizontalWrapMode.Wrap;   // длинный текст не вылезает за поле
        f.txt.verticalOverflow = VerticalWrapMode.Truncate;
        return f;
    }

    // строка «Аккаунт» в настройках
    void BuildAccountRow(RectTransform parent, float y)
    {
        var card = TL(parent, 20, y, 350, 64, "Account");
        Round(card, Color.white, 22);
        setAccTxt = Label(card, "", 14, Ink, TextAnchor.MiddleLeft, 16, 0, 230, 64);
        var b = TL(card, 252, 14, 86, 36, "Open");
        Round(b, C(238, 227, 247), 18);
        Label(b, "Открыть", 14, Ink, TextAnchor.MiddleCenter, 0, 0, 86, 36);
        AddBtn(b, () => SetTab(TabAccount), 3);
    }

    void AccMessage(string s, bool error, bool good = false)
    {
        var t = HasAccount ? accUserMsg : accMsg;
        if (t == null) return;
        if (accMsg != null && t != accMsg) accMsg.text = "";
        t.text = s;
        t.color = error ? Red : good ? C(60, 150, 110) : Muted;
    }

    void RefreshAccount()
    {
        if (setAccTxt != null) setAccTxt.text = HasAccount ? "Аккаунт: " + accEmail : "Аккаунт: вход не выполнен";
        if (tab == TabStart) RefreshStartMenu();
        if (accountPanel == null || tab != TabAccount) return;

        bool logged = HasAccount;
        accGuest.gameObject.SetActive(!logged);
        accUser.gameObject.SetActive(logged);

        for (int i = 0; i < 2; i++)
        {
            bool sel = (i == 1) == accRegisterMode;
            accModeImg[i].color = sel ? Ink : Color.white;
            accModeTxt[i].color = sel ? Color.white : Ink;
        }
        accMainTxt.text = accRegisterMode ? "Создать аккаунт" : "Войти";
        accMainImg.color = accBusy ? C(216, 201, 190) : Red;
        accHint.text = accRegisterMode
            ? "Текущий прогресс сохранится в новом аккаунте. Пароль — не короче 6 символов.\nДетям лучше вводить почту и пароль вместе со взрослым."
            : "Прогресс на этом устройстве заменится прогрессом из аккаунта.\nДетям лучше вводить почту и пароль вместе со взрослым.";

        accEmailTxt.text = accEmail;
        accIdTxt.text = "Мой ID: " + netId;
        accSyncTxt.text = netState != NetState.Online ? "Нет связи с сервером — сохраним, когда связь появится"
            : accDirty ? "Сохраняем изменения…" : "Прогресс сохранён на сервере";
        accSyncTxt.color = netState != NetState.Online ? Red : Ink;
        RefreshFields();
    }

    void RefreshFields()
    {
        if (accEmailField == null) return;
        foreach (var f in new[] { accEmailField, accPassField })
        {
            bool foc = f == focused;
            bool caret = foc && tsk == null && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
            if (f.value.Length == 0 && !foc) { f.txt.text = f.placeholder; f.txt.color = Muted; }
            else
            {
                string shown = f.secret && !f.reveal ? new string('•', f.value.Length) : f.value;
                f.txt.text = shown + (caret ? "|" : "");
                f.txt.color = Ink;
            }
            f.bg.color = foc ? C(244, 236, 252) : Color.white;
        }
        accShowTxt.text = accPassField.reveal ? "Скрыть" : "Показать";
    }

    // ---------- ввод текста ----------
    void FocusField(TextField f)
    {
        if (focused == f && tsk != null) return;
        Blur();
        focused = f;
        GameAudio.Click();
        // в редакторе (в том числе в Device Simulator) печатаем с клавиатуры компьютера
        if (TouchScreenKeyboard.isSupported && !Application.isEditor)
            tsk = TouchScreenKeyboard.Open(f.value, f.kb, false, false, f.secret, false, f.placeholder);
        RefreshFields();
    }

    void Blur()
    {
        if (tsk != null) { tsk.active = false; tsk = null; }
        focused = null;
        if (accEmailField != null) RefreshFields();
    }

    void TypeChar(char c)
    {
        if (focused == null || char.IsControl(c) || focused.value.Length >= focused.max) return;
        if (focused == accEmailField && char.IsWhiteSpace(c)) return;
        focused.value += c;
    }

    void FieldBackspace()
    {
        if (focused != null && focused.value.Length > 0) focused.value = focused.value.Substring(0, focused.value.Length - 1);
    }

    void FieldEnter()
    {
        if (focused == accEmailField) FocusField(accPassField);
        else AccSubmit();
    }

    void FieldPaste()
    {
        foreach (char c in GUIUtility.systemCopyBuffer ?? "") TypeChar(c);
    }

    // вызывается каждый кадр из Update
    void UpdateAccount(float dt)
    {
        if (logoutArm > 0f)
        {
            logoutArm -= dt;
            if (logoutArm <= 0f && accLogoutTxt != null) accLogoutTxt.text = "Выйти из аккаунта";
        }

        if (tab != TabAccount || focused == null)
        {
            if (focused != null) Blur();
            return;
        }

        if (tsk != null)
        {
            // телефон: текст берём из системной клавиатуры
            string t = tsk.text ?? "";
            if (t.Length > focused.max) t = t.Substring(0, focused.max);
            focused.value = focused == accEmailField ? t.Trim() : t;
            if (tsk.status == TouchScreenKeyboard.Status.Done)
            {
                var f = focused;
                tsk = null;
                focused = null;
                if (f == accPassField) AccSubmit();
            }
            else if (tsk.status != TouchScreenKeyboard.Status.Visible) { tsk = null; focused = null; }
        }
        RefreshFields();
    }

    // компьютер (и редактор Unity): буквы приходят как события клавиатуры IMGUI —
    // так ввод работает и в Game view, и в Device Simulator, при любой системе ввода
    void OnGUI()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown) return;
        if (tab == TabPlayers) { if (PlayersKey(e)) e.Use(); return; }
        if (focused == null || tsk != null || tab != TabAccount) return;

        if ((e.control || e.command) && e.keyCode == KeyCode.V) FieldPaste();
        else if (e.keyCode == KeyCode.Backspace) FieldBackspace();
        else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) FieldEnter();
        else if (e.keyCode == KeyCode.Tab) FocusField(focused == accEmailField ? accPassField : accEmailField);
        else if (e.character != '\0' && !e.control && !e.command) TypeChar(e.character);
        else return;
        e.Use();
        RefreshFields();
    }
}
