// Связь с сервером игроков (папка Server в корне проекта, запускается на ПК).
//
// • Адрес сервера ищется сам: сначала адреса из Assets/Resources/server_url.txt (по порядку),
//   потом последний рабочий адрес, потом поиск в локальной сети (UDP-запрос), потом 127.0.0.1.
// • При первом подключении игрок получает 6-значный ID и секретный ключ (хранятся в PlayerPrefs).
// • Каждые 20 секунд игра отправляет свой статус: имя, аватар, уровень, животных, монеты, кристаллы.
//   Сервер считает игрока «в сети», пока такие сообщения приходят.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
#if !UNITY_WEBGL
using System.Net;
using System.Net.Sockets;
using System.Threading;
#endif

[Serializable] class NetRegisterReq { public int nickA, nickB; }
[Serializable] class NetRegisterResp { public bool ok; public string id; public string token; public string error; }
[Serializable] class NetPing { public bool ok; public string game; }
[Serializable] class NetResult { public bool ok; public string error; }

[Serializable]
class NetStatusReq
{
    public string id, token;
    public int nickA, nickB, avatar, level, zoo, zooTotal, score, gems, bestEgg;
    public bool online;
}

[Serializable]
class NetPlayer
{
    public bool ok, found, online, mutual;
    public string id;
    public int nickA, nickB, avatar, level, zoo, zooTotal, score, gems, bestEgg, lastSeenSec, sinceDays, friends;
}

public partial class MergeGame
{
    const int ServerPort = 8080, DiscoveryPort = 47777;
    const float Heartbeat = 20f, RetryDelay = 10f;

    enum NetState { Connecting, Online, Offline }

    NetState netState = NetState.Connecting;
    string netUrl, netId, netToken;
    int nickA, nickB;
    bool netKick, netStarted;
    Text serverTxt;
    Image serverDot;

    void InitNet()
    {
        netId = PlayerPrefs.GetString("net_id", "");
        netToken = PlayerPrefs.GetString("net_token", "");
        nickA = PlayerPrefs.GetInt("net_nickA", -1);
        nickB = PlayerPrefs.GetInt("net_nickB", -1);
        if (nickA < 0 || nickA >= NickA.Length || nickB < 0 || nickB >= NickB.Length)
        {
            nickA = UnityEngine.Random.Range(0, NickA.Length);
            nickB = UnityEngine.Random.Range(0, NickB.Length);
            PlayerPrefs.SetInt("net_nickA", nickA);
            PlayerPrefs.SetInt("net_nickB", nickB);
        }
        netUrl = PlayerPrefs.GetString("net_url", "");
        InitAccount();
        InitModeration();
        netStarted = true;
        RefreshNetUi();
        StartCoroutine(NetLoop());
    }

    void KickNet() { netKick = true; }

    IEnumerator NetLoop()
    {
        float lastStatus = -999f;
        while (true)
        {
            if (netState != NetState.Online)
            {
                yield return Connect();
                lastStatus = -999f;
                if (netState != NetState.Online) netKick = false;
            }
            // у аккаунта сначала сверяем облачное сохранение — иначе загрузка с сервера
            // перезапишет то, что успело измениться (например, подарок администратора)
            if (netState == NetState.Online && HasAccount && !accLoaded) yield return SyncSave();

            // статус «в сети» — раз в 20 секунд или сразу по KickNet()
            if (netState == NetState.Online && (netKick || Time.unscaledTime - lastStatus >= Heartbeat))
            {
                netKick = false;
                lastStatus = Time.unscaledTime;
                yield return SendStatus(true);
            }
            // облачное сохранение — проверяется каждые 3 секунды
            if (netState == NetState.Online && HasAccount) yield return SyncSave();

            float wait = netState == NetState.Online ? 3f : RetryDelay, t = 0f;
            while (t < wait && !netKick) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }

    // ---------- Поиск сервера и регистрация ----------
    IEnumerator Connect()
    {
        SetNetState(NetState.Connecting);
        string found = null;

        var candidates = new System.Collections.Generic.List<string>(ConfiguredUrls()) { netUrl };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate) || found != null) continue;
            bool ok = false;
            yield return Ping(candidate, r => ok = r);
            if (ok) found = candidate;
        }

        if (found == null)
        {
            string lan = null;
            yield return Discover(r => lan = r);
            if (lan != null)
            {
                bool ok = false;
                yield return Ping(lan, r => ok = r);
                if (ok) found = lan;
            }
        }

        if (found == null)
        {
            string local = "http://127.0.0.1:" + ServerPort;
            bool ok = false;
            yield return Ping(local, r => ok = r);
            if (ok) found = local;
        }

        if (found == null) { SetNetState(NetState.Offline); yield break; }

        netUrl = found;
        PlayerPrefs.SetString("net_url", netUrl);

        if (string.IsNullOrEmpty(netId) || string.IsNullOrEmpty(netToken))
        {
            NetRegisterResp reg = null;
            string body = JsonUtility.ToJson(new NetRegisterReq { nickA = nickA, nickB = nickB });
            yield return Http("POST", netUrl, "/api/register", body, (code, text) =>
            {
                if (code == 200) reg = Parse<NetRegisterResp>(text);
            });
            if (reg == null || !reg.ok || string.IsNullOrEmpty(reg.id)) { SetNetState(NetState.Offline); yield break; }
            netId = reg.id;
            netToken = reg.token;
            PlayerPrefs.SetString("net_id", netId);
            PlayerPrefs.SetString("net_token", netToken);
            PlayerPrefs.Save();
        }

        SetNetState(NetState.Online);
    }

    // все адреса из server_url.txt по порядку: игра пробует их, пока один не ответит
    static string[] ConfiguredUrls()
    {
        var list = new System.Collections.Generic.List<string>();
        var ta = Resources.Load<TextAsset>("server_url");
        if (ta == null) return list.ToArray();
        foreach (var raw in ta.text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.Contains(" ")) continue;
            if (!line.StartsWith("http://") && !line.StartsWith("https://")) line = "http://" + line;
            list.Add(line.TrimEnd('/'));
        }
        return list.ToArray();
    }

    IEnumerator Ping(string baseUrl, Action<bool> done)
    {
        bool ok = false;
        yield return Http("GET", baseUrl, "/api/ping", null, (code, text) =>
        {
            var p = code == 200 ? Parse<NetPing>(text) : null;
            ok = p != null && p.ok && p.game == "hatchmerge";
        }, 2);
        done(ok);
    }

    // Поиск сервера в локальной сети: широковещательный UDP-запрос, сервер отвечает своим портом.
    IEnumerator Discover(Action<string> done)
    {
#if UNITY_WEBGL
        done(null);
        yield break;
#else
        string result = null;
        bool finished = false;
        var th = new Thread(() =>
        {
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.EnableBroadcast = true;
                    udp.Client.ReceiveTimeout = 1500;
                    byte[] msg = Encoding.UTF8.GetBytes("HATCHMERGE_DISCOVER");
                    udp.Send(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    string reply = Encoding.UTF8.GetString(udp.Receive(ref from));
                    if (reply.StartsWith("HATCHMERGE_SERVER"))
                    {
                        string[] parts = reply.Split(' ');
                        string port = parts.Length > 1 ? parts[1].Trim() : ServerPort.ToString();
                        result = "http://" + from.Address + ":" + port;
                    }
                }
            }
            catch (Exception) { }
            finished = true;
        }) { IsBackground = true };
        th.Start();

        float t = 0f;
        while (!finished && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
        done(result);
#endif
    }

    // ---------- Статус ----------
    NetStatusReq MyStatus(bool online) => new NetStatusReq
    {
        id = netId, token = netToken, nickA = nickA, nickB = nickB,
        avatar = MyAvatar(), level = zooCount + 1, zoo = zooCount, zooTotal = Zoo.Length,
        score = score, gems = gems, bestEgg = BestLevel(), online = online,
    };

    IEnumerator SendStatus(bool online)
    {
        long result = -1;
        string body = null;
        yield return Http("POST", netUrl, "/api/status", JsonUtility.ToJson(MyStatus(online)), (code, text) => { result = code; body = text; });
        var resp = Parse<NetStatusResp>(body);

        if (result == 200)
        {
            SetNetState(NetState.Online);
            if (banned) SetBanned(false, "");                       // блокировку сняли
            // подарки применяем, только когда сохранение аккаунта уже сверено (иначе — в следующий раз)
            if (resp != null && resp.grants != null && resp.grants.Length > 0 && (!HasAccount || accLoaded))
                yield return ApplyGrants(resp.grants);
            yield break;
        }
        if (result == 403 && resp != null && resp.error == "banned")
        {
            SetNetState(NetState.Online);
            SetBanned(true, resp.reason);
            yield break;
        }
        if (result == 403 || result == 404)
        {
            // сервер нас не знает (например, база была удалена) — зарегистрируемся заново
            netId = netToken = "";
            PlayerPrefs.DeleteKey("net_id");
            PlayerPrefs.DeleteKey("net_token");
            ForgetAccountLocal();
            SetNetState(NetState.Connecting);
            netKick = true;
            yield break;
        }
        SetNetState(NetState.Offline);
    }

    // при выходе или сворачивании — сообщить «не в сети» (без ожидания ответа)
    void NetGoodbye()
    {
        if (!netStarted || netState != NetState.Online || string.IsNullOrEmpty(netId)) return;
        AccountGoodbye();
        try
        {
            var req = MakeRequest("POST", netUrl + "/api/status", JsonUtility.ToJson(MyStatus(false)), 2);
            req.SendWebRequest().completed += _ => req.Dispose();
        }
        catch (Exception) { }
    }

    // ---------- Поиск игрока ----------
    IEnumerator FetchPlayer(string id, bool userAction)
    {
        if (userAction) ShowFoundMsg("Ищем игрока " + id + "…");
        long code = 0; string text = null;
        yield return Http("GET", netUrl, "/api/player/" + id, null, (c, t) => { code = c; text = t; });

        if (id != shownId) yield break;          // пока ждали ответ, начали искать другого
        if (code == 200)
        {
            var p = Parse<NetPlayer>(text);
            if (p != null && p.found) { ShowFound(p); yield break; }
        }
        if (code == 200 || code == 404) { ShowFoundMsg("Игрок с ID " + id + " не найден"); shownId = null; }
        else if (userAction) ShowFoundMsg("Нет связи с сервером. Попробуйте ещё раз");
    }

    // ---------- HTTP ----------
    static UnityWebRequest MakeRequest(string method, string url, string json, int timeout)
    {
        var req = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = timeout };
        if (json != null)
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.SetRequestHeader("Content-Type", "application/json");
        }
        return req;
    }

    // done(code, text): code = 0, если сервер недоступен
    static IEnumerator Http(string method, string baseUrl, string path, string json, Action<long, string> done, int timeout = 5)
    {
        if (string.IsNullOrEmpty(baseUrl)) { done(0, null); yield break; }
        using (var req = MakeRequest(method, baseUrl + path, json, timeout))
        {
            yield return req.SendWebRequest();
            bool noAnswer = req.result == UnityWebRequest.Result.ConnectionError || req.responseCode == 0;
            done(noAnswer ? 0 : req.responseCode, req.downloadHandler != null ? req.downloadHandler.text : null);
        }
    }

    static T Parse<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<T>(json); }
        catch (Exception) { return null; }
    }

    // ---------- Отображение состояния ----------
    void SetNetState(NetState s)
    {
        if (netState == s) return;
        netState = s;
        RefreshNetUi();
    }

    string NetStateText()
    {
        switch (netState)
        {
            case NetState.Online: return "в сети";
            case NetState.Connecting: return "подключение…";
            default: return "нет связи с сервером";
        }
    }

    Color NetStateColor() =>
        netState == NetState.Online ? C(110, 200, 160) : netState == NetState.Connecting ? C(255, 201, 120) : C(220, 120, 110);

    void RefreshNetUi()
    {
        if (tab == TabStart) RefreshStartNet();
        if (tab == TabPlayers) RefreshMyCard();
        RefreshAccount();
        if (serverTxt != null)
        {
            string addr = string.IsNullOrEmpty(netUrl) ? "не найден" : netUrl.Replace("http://", "");
            serverTxt.text = netState == NetState.Online ? "Сервер: " + addr : "Сервер: " + NetStateText();
            serverDot.color = NetStateColor();
        }
    }

    // строка «Сервер игроков» в настройках
    void BuildServerRow(RectTransform parent, float y)
    {
        var card = TL(parent, 20, y, 350, 64, "Server");
        Round(card, Color.white, 22);
        serverDot = AddImg(TL(card, 16, 27, 10, 10, "Dot"), circleSpr, Muted);
        serverTxt = Label(card, "Сервер: …", 14, Ink, TextAnchor.MiddleLeft, 34, 0, 220, 64);
        var b = TL(card, 252, 14, 86, 36, "Reconnect");
        Round(b, C(238, 227, 247), 18);
        Label(b, "Найти", 14, Ink, TextAnchor.MiddleCenter, 0, 0, 86, 36);
        AddBtn(b, () =>
        {
            GameAudio.Click();
            netUrl = "";                       // забыть старый адрес и искать заново
            SetNetState(NetState.Connecting);
            KickNet();
        }, 3);
    }
}
