#!/usr/bin/env python3
"""Сервер игроков для «Яйца-сюрпризы» (HatchMerge).

Запуск:  python hatchmerge_server.py          (или двойной щелчок по start_server.bat)
Нужен только Python 3.8+, сторонние библиотеки не нужны.

Что делает:
  * выдаёт каждому игроку 6-значный ID;
  * хранит имя, аватар и прогресс игроков в файле hatchmerge.db рядом со скриптом;
  * отвечает на поиск игрока по ID и сообщает, в сети ли он;
  * отвечает игре на автопоиск в локальной сети (UDP, порт 47777).

API (JSON):
  GET  /api/ping                 -> {"ok": true, "game": "hatchmerge"}
  POST /api/register             {"nickA","nickB"}             -> {"ok", "id", "token"}
  POST /api/status               {"id","token", ...прогресс}   -> {"ok"}
  GET  /api/player/<ID>          -> {"ok","found", ...статус}
  POST /api/account/register     {"id","token","email","password"} -> {"ok","email"}   привязать почту к игроку
  POST /api/account/login        {"email","password"}          -> {"ok","id","token","email","save","saveVer",...}
  POST /api/load                 {"id","token"}                -> {"ok","save","saveVer","email"}
  POST /api/save                 {"id","token","save","baseVer"} -> {"ok","saveVer"}  (409 — на сервере новее)
  POST /api/account/reset/request {"email"}                    -> {"ok"}   письмо с кодом (503 email_disabled — почта не настроена)
  POST /api/account/reset/confirm {"email","code","password"} -> как вход: {"ok","id","token",...}
  POST /api/grants/ack           {"id","token","ids":[...]}    -> {"ok"}   подарки администратора получены
  Ответ /api/status содержит "grants" — начисления/списания от администратора, ещё не полученные игрой.
  Заблокированный игрок получает 403 {"error":"banned","reason":...}.
  GET  /                         -> страница со списком игроков (для проверки в браузере)
"""

import hashlib
import hmac
import html
import smtplib
import ssl
from email.message import EmailMessage
from urllib.parse import parse_qs, quote
import json
import os
import re
import secrets
import socket
import sqlite3
import sys
import threading
import time
from collections import defaultdict, deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# Настройки через переменные окружения (на VPS их задаёт hatchmerge.service):
#   HATCHMERGE_HOST         адрес, на котором слушать (0.0.0.0 — вся сеть, 127.0.0.1 — только за Caddy)
#   HATCHMERGE_PORT         порт (8080)
#   HATCHMERGE_DB           путь к файлу базы
#   HATCHMERGE_PROXY=1      сервер стоит за Caddy: адрес игрока берётся из X-Forwarded-For
#   HATCHMERGE_DISCOVERY=0  выключить автопоиск в локальной сети (на VPS он не нужен)
#   HATCHMERGE_LIST=0       не показывать список игроков на главной странице (для интернета)
#   HATCHMERGE_SMTP_HOST, HATCHMERGE_SMTP_PORT, HATCHMERGE_SMTP_USER, HATCHMERGE_SMTP_PASSWORD,
#   HATCHMERGE_SMTP_FROM    почтовый ящик для писем с кодом сброса пароля (без них сброс — только через /admin)
#   HATCHMERGE_ADMIN_PASSWORD  пароль страницы администратора /admin
#                           (на VPS задаётся в /etc/hatchmerge.env; без пароля /admin открыт только
#                           при запуске на своём ПК, а за Caddy — выключен)
HTTP_HOST = os.environ.get("HATCHMERGE_HOST", "0.0.0.0")
HTTP_PORT = int(os.environ.get("HATCHMERGE_PORT", "8080"))
BEHIND_PROXY = os.environ.get("HATCHMERGE_PROXY", "0") == "1"
DISCOVERY = os.environ.get("HATCHMERGE_DISCOVERY", "1") == "1"
PUBLIC_LIST = os.environ.get("HATCHMERGE_LIST", "1") == "1"
ADMIN_PASSWORD = os.environ.get("HATCHMERGE_ADMIN_PASSWORD", "")
ADMIN_SESSION_HOURS = 12
SMTP_HOST = os.environ.get("HATCHMERGE_SMTP_HOST", "")
SMTP_PORT = int(os.environ.get("HATCHMERGE_SMTP_PORT", "465") or 465)
SMTP_USER = os.environ.get("HATCHMERGE_SMTP_USER", "")
SMTP_PASSWORD = os.environ.get("HATCHMERGE_SMTP_PASSWORD", "")
SMTP_FROM = os.environ.get("HATCHMERGE_SMTP_FROM", "") or SMTP_USER
RESET_CODE_MINUTES = 15
RESET_MAX_ATTEMPTS = 5
DISCOVERY_PORT = 47777
ONLINE_WINDOW = 70          # секунд без сообщений, после которых игрок считается «не в сети»
NICK_A_COUNT = NICK_B_COUNT = 16
ZOO_MAX = 8
MAX_BODY = 16384
MAX_SAVE = 8192            # размер сохранения игры, байт
PBKDF2_ROUNDS = 200_000
DB_PATH = os.environ.get("HATCHMERGE_DB") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "hatchmerge.db")

NICK_A = ["Весёлый", "Смелый", "Быстрый", "Хитрый", "Добрый", "Сонный", "Звёздный", "Солнечный",
          "Пушистый", "Ловкий", "Храбрый", "Шустрый", "Радужный", "Мятный", "Ягодный", "Облачный"]
NICK_B = ["Котик", "Зайчик", "Ёжик", "Мишка", "Пингвин", "Дракончик", "Енотик", "Лисёнок",
          "Совёнок", "Тигрёнок", "Слонёнок", "Бельчонок", "Утёнок", "Пёсик", "Хомячок", "Единорожек"]

# ---------------------------------------------------------------- база данных

_db_lock = threading.Lock()
_db = sqlite3.connect(DB_PATH, check_same_thread=False)
_db.row_factory = sqlite3.Row
_db.execute("""
    CREATE TABLE IF NOT EXISTS players (
        id        TEXT PRIMARY KEY,
        token     TEXT NOT NULL,
        nick_a    INTEGER NOT NULL DEFAULT 0,
        nick_b    INTEGER NOT NULL DEFAULT 0,
        avatar    INTEGER NOT NULL DEFAULT -1,
        level     INTEGER NOT NULL DEFAULT 1,
        zoo       INTEGER NOT NULL DEFAULT 0,
        zoo_total INTEGER NOT NULL DEFAULT 8,
        score     INTEGER NOT NULL DEFAULT 0,
        gems      INTEGER NOT NULL DEFAULT 0,
        best_egg  INTEGER NOT NULL DEFAULT 0,
        online    INTEGER NOT NULL DEFAULT 0,
        last_seen REAL    NOT NULL,
        created   REAL    NOT NULL
    )""")
_db.execute("""
    CREATE TABLE IF NOT EXISTS accounts (
        email     TEXT PRIMARY KEY,
        pass_hash TEXT NOT NULL,
        player_id TEXT NOT NULL UNIQUE,
        created   REAL NOT NULL
    )""")
# новые столбцы для облачного сохранения (база от прошлой версии обновляется сама)
_cols = {r[1] for r in _db.execute("PRAGMA table_info(players)")}
if "save" not in _cols:
    _db.execute("ALTER TABLE players ADD COLUMN save TEXT NOT NULL DEFAULT ''")
if "save_ver" not in _cols:
    _db.execute("ALTER TABLE players ADD COLUMN save_ver INTEGER NOT NULL DEFAULT 0")
for _col, _sql in (("banned", "INTEGER NOT NULL DEFAULT 0"), ("ban_reason", "TEXT NOT NULL DEFAULT ''"),
                   ("banned_at", "REAL NOT NULL DEFAULT 0")):
    if _col not in _cols:
        _db.execute(f"ALTER TABLE players ADD COLUMN {_col} {_sql}")
_db.execute("""
    CREATE TABLE IF NOT EXISTS grants (
        id         INTEGER PRIMARY KEY AUTOINCREMENT,
        player_id  TEXT NOT NULL,
        coins      INTEGER NOT NULL DEFAULT 0,
        gems       INTEGER NOT NULL DEFAULT 0,
        energy     INTEGER NOT NULL DEFAULT 0,
        note       TEXT NOT NULL DEFAULT '',
        created    REAL NOT NULL,
        applied_at REAL,
        canceled   INTEGER NOT NULL DEFAULT 0
    )""")
_db.execute("CREATE INDEX IF NOT EXISTS grants_player ON grants(player_id, applied_at)")
_db.execute("""
    CREATE TABLE IF NOT EXISTS reset_codes (
        email     TEXT PRIMARY KEY,
        code_hash TEXT NOT NULL,
        expires   REAL NOT NULL,
        attempts  INTEGER NOT NULL DEFAULT 0
    )""")
_db.execute("""
    CREATE TABLE IF NOT EXISTS admin_log (
        id        INTEGER PRIMARY KEY AUTOINCREMENT,
        ts        REAL NOT NULL,
        ip        TEXT NOT NULL,
        player_id TEXT NOT NULL,
        action    TEXT NOT NULL,
        details   TEXT NOT NULL DEFAULT ''
    )""")
_db.commit()


def clamp_int(value, lo, hi, default):
    try:
        v = int(value)
    except (TypeError, ValueError):
        return default
    return max(lo, min(hi, v))


def new_id():
    while True:
        pid = str(secrets.randbelow(900000) + 100000)
        if _db.execute("SELECT 1 FROM players WHERE id=?", (pid,)).fetchone() is None:
            return pid


def player_json(row, now):
    online = bool(row["online"]) and now - row["last_seen"] < ONLINE_WINDOW
    return {
        "ok": True, "found": True, "id": row["id"],
        "nickA": row["nick_a"], "nickB": row["nick_b"], "avatar": row["avatar"],
        "level": row["level"], "zoo": row["zoo"], "zooTotal": row["zoo_total"],
        "score": row["score"], "gems": row["gems"], "bestEgg": row["best_egg"],
        "online": online, "lastSeenSec": int(max(0, now - row["last_seen"])),
    }


EMAIL_RE = re.compile(r"^[^@\s]{1,64}@[^@\s]+\.[^@\s]{2,}$")


def norm_email(value):
    email = str(value or "").strip().lower()
    return email if len(email) <= 254 and EMAIL_RE.match(email) else None


def hash_password(password, salt=None):
    salt = salt or secrets.token_bytes(16)
    digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, PBKDF2_ROUNDS)
    return f"pbkdf2${PBKDF2_ROUNDS}${salt.hex()}${digest.hex()}"


def check_password(password, stored):
    try:
        _, rounds, salt_hex, digest_hex = stored.split("$")
        digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), bytes.fromhex(salt_hex), int(rounds))
        return hmac.compare_digest(digest.hex(), digest_hex)
    except (ValueError, TypeError):
        return False


DUMMY_HASH = hash_password("dummy-password")   # для входа с несуществующей почтой


def valid_save(value):
    """Сохранение — JSON-объект в виде строки, не больше MAX_SAVE байт."""
    if not isinstance(value, str) or len(value.encode("utf-8")) > MAX_SAVE:
        return None
    try:
        return value if isinstance(json.loads(value), dict) else None
    except ValueError:
        return None


ZOO_NAMES = ["Облачко-пушистик", "Дракончик-тапок", "Медузка-шарик", "Лисёнок-носок",
             "Котик-клубочек", "Совёнок-пончик", "Зайка-варежка", "Единорожек-пирожок"]
EGG_COLORS = ["", "#f6d6a8", "#9ed8c6", "#8ec5f2", "#f5a7c8", "#c7a6f2", "#ffc978", "#ff8c78"]


def fmt_time(ts):
    return time.strftime("%d.%m.%Y %H:%M", time.localtime(ts)) if ts else "—"


def ago(sec):
    if sec < 60:
        return "только что"
    if sec < 3600:
        return f"{sec // 60} мин назад"
    if sec < 86400:
        return f"{sec // 3600} ч назад"
    return f"{sec // 86400} дн назад"


ADMIN_CSS = """
:root{--bg:#fff3e0;--ink:#3b2a4a;--muted:#8a7a96;--card:#fff;--line:#f0e6f6;--accent:#c93c20;--on:#3c9a6e}
@media (prefers-color-scheme:dark){:root{--bg:#1e1724;--ink:#f1e8f7;--muted:#a597b0;--card:#2a2132;--line:#3a2f44}}
*{box-sizing:border-box}body{margin:0;font-family:system-ui,-apple-system,Segoe UI,sans-serif;background:var(--bg);color:var(--ink)}
header{display:flex;align-items:center;justify-content:space-between;padding:14px 20px;background:var(--card);border-bottom:1px solid var(--line)}
header h1{font-size:18px;margin:0}main{padding:20px;max-width:1300px;margin:auto}
a{color:var(--accent)}.muted{color:var(--muted)}.err{color:var(--accent)}
.card{background:var(--card);border-radius:16px;padding:16px 18px;box-shadow:0 1px 3px #0001}
.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;margin-bottom:16px}
.stats b{display:block;font-size:28px}
.search{display:flex;gap:8px;align-items:center;margin:0 0 12px}
input{font:inherit;padding:10px 12px;border-radius:10px;border:1px solid var(--line);background:var(--card);color:var(--ink);min-width:260px}
button{font:inherit;padding:10px 16px;border:0;border-radius:10px;background:var(--accent);color:#fff;cursor:pointer}
.wrap{overflow-x:auto;background:var(--card);border-radius:16px}
table{border-collapse:collapse;width:100%}th,td{padding:9px 12px;text-align:left;border-bottom:1px solid var(--line);white-space:nowrap}
th{font-size:12px;color:var(--muted);font-weight:600}td.n{text-align:right;font-variant-numeric:tabular-nums}
.wrap tr:hover td{background:var(--line);cursor:pointer}
.on{color:var(--on)}.off{color:var(--muted)}
.login{max-width:360px;margin:60px auto;display:flex;flex-direction:column;gap:12px}.login input{min-width:0}
.cols{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px}
.kv th{width:45%}.kv td{white-space:normal}
.grid{display:grid;grid-template-columns:repeat(5,34px);gap:4px}
.cell{width:34px;height:34px;border-radius:10px;border:1px solid var(--line);display:flex;align-items:center;justify-content:center;font-weight:700;color:#3b2a4a}
.zoo{list-style:none;padding:0;display:grid;grid-template-columns:1fr 1fr;gap:6px}
.zoo li{padding:6px 10px;border-radius:10px;background:var(--line)}.zoo li.lock{opacity:.45}
.zoo li.got::before{content:"✓ ";color:var(--on)}
.ban{color:var(--accent);font-weight:600}.plus{color:var(--on)}.minus{color:var(--accent)}
.flash{border-left:4px solid var(--on)}.secret{border-left-color:var(--accent)}
.pw{font-family:ui-monospace,Consolas,monospace;font-size:20px;letter-spacing:1px;user-select:all}.row{display:flex;gap:8px;flex-wrap:wrap}.row input{flex:1}
button.ok{background:var(--on)}button.small{padding:4px 10px;font-size:13px}
.grantf{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;align-items:end;margin-bottom:8px}
.grantf label{display:flex;flex-direction:column;gap:4px;font-size:13px;color:var(--muted)}
.grantf input{min-width:0}.grantf .wide{grid-column:1/3}.grantf button{height:42px}
.wrap2{overflow-x:auto}h3{margin:18px 0 10px}.card h3:first-child{margin-top:0}
@media (max-width:600px){main{padding:12px}input{min-width:0;flex:1}.grantf{grid-template-columns:1fr 1fr}.grantf .wide{grid-column:1/-1}}
"""


def admin_shell(title, body, refresh=0):
    meta = f'<meta http-equiv="refresh" content="{refresh}">' if refresh else ""
    logout = ('<form method=post action="/admin/logout" style="margin:0"><button>Выйти</button></form>'
              if ADMIN_PASSWORD else "")
    return f"""<!doctype html><html lang=ru><meta charset=utf-8>
<meta name=viewport content="width=device-width,initial-scale=1">{meta}
<meta name=robots content="noindex,nofollow"><title>{html.escape(title)} · HatchMerge</title>
<style>{ADMIN_CSS}</style><header><h1>Яйца-сюрпризы · администратор</h1>{logout}</header>
<main>{body}</main></html>"""


def smtp_enabled():
    return bool(SMTP_HOST and SMTP_USER and SMTP_PASSWORD)


def send_mail(to, subject, text):
    """Отправить письмо (в отдельном потоке, чтобы не задерживать ответ игре)."""
    def work():
        msg = EmailMessage()
        msg["From"] = f"Яйца-сюрпризы <{SMTP_FROM}>"
        msg["To"] = to
        msg["Subject"] = subject
        msg.set_content(text)
        try:
            ctx = ssl.create_default_context()
            if SMTP_PORT == 465:
                with smtplib.SMTP_SSL(SMTP_HOST, SMTP_PORT, timeout=20, context=ctx) as smtp:
                    smtp.login(SMTP_USER, SMTP_PASSWORD)
                    smtp.send_message(msg)
            else:
                with smtplib.SMTP(SMTP_HOST, SMTP_PORT, timeout=20) as smtp:
                    smtp.starttls(context=ctx)
                    smtp.login(SMTP_USER, SMTP_PASSWORD)
                    smtp.send_message(msg)
            print(f"{time.strftime('%H:%M:%S')}  письмо с кодом отправлено")
        except Exception as e:                      # noqa: BLE001 — любую ошибку почты только пишем в журнал
            print(f"{time.strftime('%H:%M:%S')}  НЕ удалось отправить письмо: {e}")
    threading.Thread(target=work, daemon=True).start()


def code_hash(email, code):
    return hashlib.sha256(f"{email}:{code}".encode("utf-8")).hexdigest()


def temp_password():
    alphabet = "abcdefghjkmnpqrstuvwxyz23456789"          # без похожих букв и цифр
    return "".join(secrets.choice(alphabet) for _ in range(8))


def admin_log(ip, pid, action, details):
    """Записать действие администратора (вызывать под _db_lock)."""
    _db.execute("INSERT INTO admin_log (ts, ip, player_id, action, details) VALUES (?,?,?,?,?)",
                (time.time(), ip, pid, action, details))


def login_json(row, email):
    return {"ok": True, "id": row["id"], "token": row["token"], "email": email,
            "nickA": row["nick_a"], "nickB": row["nick_b"], "save": row["save"], "saveVer": row["save_ver"]}


def banned_json(row):
    return {"ok": False, "error": "banned", "reason": row["ban_reason"] or ""}


def nick(row):
    return f"{NICK_A[row['nick_a'] % len(NICK_A)]} {NICK_B[row['nick_b'] % len(NICK_B)]}"

# ---------------------------------------------------------------- защита от перебора


class RateLimiter:
    def __init__(self, limit, window):
        self.limit, self.window = limit, window
        self.hits = defaultdict(deque)
        self.lock = threading.Lock()

    def allow(self, key):
        now = time.time()
        with self.lock:
            q = self.hits[key]
            while q and now - q[0] > self.window:
                q.popleft()
            if len(q) >= self.limit:
                return False
            q.append(now)
            return True


register_limit = RateLimiter(20, 3600)    # регистраций с одного адреса в час
admin_login_limit = RateLimiter(8, 900)   # попыток входа администратора за 15 минут
admin_sessions = {}                        # cookie -> {"exp": время окончания, "csrf": токен форм}
LOCAL_CSRF = secrets.token_urlsafe(16)     # токен форм, когда пароль не задан (сервер на своём ПК)
LOCAL_STORE = {}                           # одноразовые сообщения, когда пароль не задан
login_ip_limit = RateLimiter(20, 600)     # попыток входа с одного адреса за 10 минут
login_email_limit = RateLimiter(10, 900)  # попыток входа в одну учётную запись за 15 минут
account_limit = RateLimiter(10, 3600)     # привязок почты с одного адреса в час
reset_ip_limit = RateLimiter(5, 3600)     # запросов кода сброса с одного адреса в час
reset_email_limit = RateLimiter(3, 3600)  # писем с кодом на одну почту в час
reset_try_limit = RateLimiter(20, 900)    # попыток ввести код с одного адреса за 15 минут
lookup_limit = RateLimiter(60, 60)        # поисков с одного адреса в минуту

# ---------------------------------------------------------------- HTTP


class Handler(BaseHTTPRequestHandler):
    server_version = "HatchMerge/1.0"

    def client_ip(self):
        if BEHIND_PROXY:
            fwd = self.headers.get("X-Forwarded-For", "")
            if fwd:
                return fwd.split(",")[-1].strip()      # последний адрес добавлен самим Caddy
        return self.client_address[0]

    def log_message(self, fmt, *args):
        if "/api/status" in self.path or "/api/save" in self.path or "/api/load" in self.path:
            # пульс и автосохранение идут часто — не засоряем консоль
            return
        sys.stdout.write("%s  %s  %s\n" % (time.strftime("%H:%M:%S"), self.client_ip(), fmt % args))

    def send_json(self, code, obj):
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(data)

    def read_json(self):
        length = clamp_int(self.headers.get("Content-Length"), 0, MAX_BODY + 1, 0)
        if length > MAX_BODY:
            return None
        try:
            obj = json.loads(self.rfile.read(length).decode("utf-8") or "{}")
            return obj if isinstance(obj, dict) else None
        except (ValueError, UnicodeDecodeError):
            return None

    def auth_player(self, body, allow_banned=False):
        """Проверить id и token из запроса. Возвращает (строку игрока, None) или (None, ответ с ошибкой)."""
        pid, token = str(body.get("id", "")), str(body.get("token", ""))
        row = _db.execute("SELECT * FROM players WHERE id=?", (pid,)).fetchone()
        if row is None:
            return None, (404, {"ok": False, "error": "unknown_player"})
        if not secrets.compare_digest(row["token"], token):
            return None, (403, {"ok": False, "error": "bad_token"})
        if row["banned"] and not allow_banned:
            return None, (403, banned_json(row))
        return row, None

    # ---- GET
    def do_GET(self):
        path = self.path.split("?", 1)[0].rstrip("/")
        if path == "/api/ping":
            return self.send_json(200, {"ok": True, "game": "hatchmerge", "version": 1})

        m = re.fullmatch(r"/api/player/(\d{6})", path)
        if m:
            if not lookup_limit.allow(self.client_ip()):
                return self.send_json(429, {"ok": False, "error": "too_many_requests"})
            with _db_lock:
                row = _db.execute("SELECT * FROM players WHERE id=?", (m.group(1),)).fetchone()
            if row is None or row["banned"]:
                return self.send_json(404, {"ok": True, "found": False})
            return self.send_json(200, player_json(row, time.time()))

        if path == "":
            return self.send_page()
        if path == "/admin":
            return self.admin_list() if self.admin_ok() else self.admin_login_page()
        m = re.fullmatch(r"/admin/player/(\d{6})", path)
        if m:
            return self.admin_player(m.group(1)) if self.admin_ok() else self.redirect("/admin")
        self.send_json(404, {"ok": False, "error": "not_found"})

    # ---- POST
    def do_POST(self):
        path = self.path.split("?", 1)[0].rstrip("/")
        if path == "/admin/login":
            return self.admin_login()
        if path == "/admin/logout":
            return self.admin_logout()
        if path.startswith("/admin/"):
            return self.admin_action(path)
        body = self.read_json()
        if body is None:
            return self.send_json(400, {"ok": False, "error": "bad_json"})

        if path == "/api/register":
            if not register_limit.allow(self.client_ip()):
                return self.send_json(429, {"ok": False, "error": "too_many_requests"})
            now = time.time()
            token = secrets.token_hex(16)
            with _db_lock:
                pid = new_id()
                _db.execute(
                    "INSERT INTO players (id, token, nick_a, nick_b, online, last_seen, created) VALUES (?,?,?,?,1,?,?)",
                    (pid, token, clamp_int(body.get("nickA"), 0, NICK_A_COUNT - 1, 0),
                     clamp_int(body.get("nickB"), 0, NICK_B_COUNT - 1, 0), now, now))
                _db.commit()
            print(f"{time.strftime('%H:%M:%S')}  новый игрок: ID {pid}")
            return self.send_json(200, {"ok": True, "id": pid, "token": token})

        if path == "/api/status":
            with _db_lock:
                row, err = self.auth_player(body, allow_banned=True)
                if err:
                    return self.send_json(*err)
                pid = row["id"]
                if row["banned"]:
                    # заблокированный: только отмечаем, что заходил, и сообщаем о блокировке
                    _db.execute("UPDATE players SET online=0, last_seen=? WHERE id=?", (time.time(), pid))
                    _db.commit()
                    return self.send_json(403, banned_json(row))
                fields = {                                   # поле в JSON -> (столбец, мин, макс)
                    "nickA": ("nick_a", 0, NICK_A_COUNT - 1), "nickB": ("nick_b", 0, NICK_B_COUNT - 1),
                    "avatar": ("avatar", -1, ZOO_MAX - 1), "level": ("level", 1, ZOO_MAX + 1),
                    "zoo": ("zoo", 0, ZOO_MAX), "zooTotal": ("zoo_total", 1, ZOO_MAX),
                    "score": ("score", 0, 2_000_000_000), "gems": ("gems", 0, 1_000_000),
                    "bestEgg": ("best_egg", 0, 7),
                }
                sets, values = ["online=?", "last_seen=?"], [1 if body.get("online", True) else 0, time.time()]
                for key, (col, lo, hi) in fields.items():
                    if key in body:                          # обновляем только присланные поля
                        sets.append(col + "=?")
                        values.append(clamp_int(body[key], lo, hi, lo))
                _db.execute(f"UPDATE players SET {', '.join(sets)} WHERE id=?", (*values, pid))
                _db.commit()
                grants = [dict(id=g["id"], coins=g["coins"], gems=g["gems"], energy=g["energy"]) for g in _db.execute(
                    "SELECT * FROM grants WHERE player_id=? AND applied_at IS NULL AND canceled=0 ORDER BY id", (pid,))]
            return self.send_json(200, {"ok": True, "grants": grants})

        if path == "/api/grants/ack":
            ids = [clamp_int(i, 0, 2**62, 0) for i in (body.get("ids") or [])[:100] if isinstance(i, (int, str))]
            with _db_lock:
                row, err = self.auth_player(body, allow_banned=True)
                if err:
                    return self.send_json(*err)
                for gid in ids:
                    _db.execute("UPDATE grants SET applied_at=? WHERE id=? AND player_id=? AND applied_at IS NULL",
                                (time.time(), gid, row["id"]))
                _db.commit()
            return self.send_json(200, {"ok": True})

        if path == "/api/account/register":
            return self.account_register(body)
        if path == "/api/account/login":
            return self.account_login(body)
        if path == "/api/account/reset/request":
            return self.reset_request(body)
        if path == "/api/account/reset/confirm":
            return self.reset_confirm(body)
        if path == "/api/load":
            with _db_lock:
                row, err = self.auth_player(body)
                if err:
                    return self.send_json(*err)
                acc = _db.execute("SELECT email FROM accounts WHERE player_id=?", (row["id"],)).fetchone()
            return self.send_json(200, {"ok": True, "save": row["save"], "saveVer": row["save_ver"],
                                        "email": acc["email"] if acc else ""})
        if path == "/api/save":
            save = valid_save(body.get("save"))
            if save is None:
                return self.send_json(400, {"ok": False, "error": "bad_save"})
            base = clamp_int(body.get("baseVer"), 0, 2**62, -1)
            with _db_lock:
                row, err = self.auth_player(body)
                if err:
                    return self.send_json(*err)
                if base != row["save_ver"]:
                    # на сервере уже есть более новое сохранение (с другого устройства)
                    return self.send_json(409, {"ok": False, "error": "conflict",
                                                "save": row["save"], "saveVer": row["save_ver"]})
                ver = row["save_ver"] + 1
                _db.execute("UPDATE players SET save=?, save_ver=? WHERE id=?", (save, ver, row["id"]))
                _db.commit()
            return self.send_json(200, {"ok": True, "saveVer": ver})

        self.send_json(404, {"ok": False, "error": "not_found"})

    # ---- учётные записи
    def account_register(self, body):
        if not account_limit.allow(self.client_ip()):
            return self.send_json(429, {"ok": False, "error": "too_many_requests"})
        email = norm_email(body.get("email"))
        password = str(body.get("password", ""))
        if email is None:
            return self.send_json(400, {"ok": False, "error": "bad_email"})
        if not 6 <= len(password) <= 128:
            return self.send_json(400, {"ok": False, "error": "weak_password"})
        pass_hash = hash_password(password)                 # медленно — вне блокировки базы
        with _db_lock:
            row, err = self.auth_player(body)
            if err:
                return self.send_json(*err)
            if _db.execute("SELECT 1 FROM accounts WHERE player_id=?", (row["id"],)).fetchone():
                return self.send_json(409, {"ok": False, "error": "already_linked"})
            if _db.execute("SELECT 1 FROM accounts WHERE email=?", (email,)).fetchone():
                return self.send_json(409, {"ok": False, "error": "email_taken"})
            _db.execute("INSERT INTO accounts (email, pass_hash, player_id, created) VALUES (?,?,?,?)",
                        (email, pass_hash, row["id"], time.time()))
            _db.commit()
        print(f"{time.strftime('%H:%M:%S')}  учётная запись для ID {row['id']}")
        return self.send_json(200, {"ok": True, "email": email})

    def account_login(self, body):
        email = norm_email(body.get("email"))
        password = str(body.get("password", ""))
        if not login_ip_limit.allow(self.client_ip()) or (email and not login_email_limit.allow(email)):
            return self.send_json(429, {"ok": False, "error": "too_many_requests"})
        with _db_lock:
            acc = _db.execute("SELECT * FROM accounts WHERE email=?", (email,)).fetchone() if email else None
        # проверяем пароль даже для несуществующей почты, чтобы по времени ответа нельзя было угадать почты
        ok = check_password(password, acc["pass_hash"] if acc else DUMMY_HASH)
        if not acc or not ok:
            return self.send_json(401, {"ok": False, "error": "bad_login"})
        with _db_lock:
            row = _db.execute("SELECT * FROM players WHERE id=?", (acc["player_id"],)).fetchone()
        if row is None:
            return self.send_json(401, {"ok": False, "error": "bad_login"})
        if row["banned"]:
            return self.send_json(403, banned_json(row))
        return self.send_json(200, login_json(row, email))

    def reset_request(self, body):
        if not smtp_enabled():
            return self.send_json(503, {"ok": False, "error": "email_disabled"})
        email = norm_email(body.get("email"))
        if email is None:
            return self.send_json(400, {"ok": False, "error": "bad_email"})
        if not reset_ip_limit.allow(self.client_ip()) or not reset_email_limit.allow(email):
            return self.send_json(429, {"ok": False, "error": "too_many_requests"})
        with _db_lock:
            acc = _db.execute("SELECT 1 FROM accounts WHERE email=?", (email,)).fetchone()
            if acc:
                code = f"{secrets.randbelow(1_000_000):06d}"
                _db.execute("INSERT OR REPLACE INTO reset_codes (email, code_hash, expires, attempts) VALUES (?,?,?,0)",
                            (email, code_hash(email, code), time.time() + RESET_CODE_MINUTES * 60))
                _db.commit()
        if acc:
            send_mail(email, f"Код для сброса пароля: {code}",
                      f"Здравствуйте!\n\nКод для сброса пароля в игре «Яйца-сюрпризы»: {code}\n"
                      f"Он действует {RESET_CODE_MINUTES} минут.\n\n"
                      "Если вы не запрашивали сброс пароля, просто не обращайте внимания на это письмо.")
        # отвечаем одинаково, есть такая почта или нет, — чтобы нельзя было проверять чужие почты
        return self.send_json(200, {"ok": True})

    def reset_confirm(self, body):
        email = norm_email(body.get("email"))
        code = re.sub(r"\D", "", str(body.get("code", "")))
        password = str(body.get("password", ""))
        if email is None:
            return self.send_json(400, {"ok": False, "error": "bad_email"})
        if not 6 <= len(password) <= 128:
            return self.send_json(400, {"ok": False, "error": "weak_password"})
        if not reset_try_limit.allow(self.client_ip()):
            return self.send_json(429, {"ok": False, "error": "too_many_requests"})
        pass_hash = hash_password(password)
        with _db_lock:
            rc = _db.execute("SELECT * FROM reset_codes WHERE email=?", (email,)).fetchone()
            if rc is None or rc["expires"] < time.time() or rc["attempts"] >= RESET_MAX_ATTEMPTS:
                return self.send_json(400, {"ok": False, "error": "bad_code"})
            if not hmac.compare_digest(rc["code_hash"], code_hash(email, code)):
                _db.execute("UPDATE reset_codes SET attempts=attempts+1 WHERE email=?", (email,))
                _db.commit()
                return self.send_json(400, {"ok": False, "error": "bad_code"})
            acc = _db.execute("SELECT * FROM accounts WHERE email=?", (email,)).fetchone()
            row = _db.execute("SELECT * FROM players WHERE id=?", (acc["player_id"],)).fetchone() if acc else None
            if row is None:
                return self.send_json(400, {"ok": False, "error": "bad_code"})
            if row["banned"]:
                return self.send_json(403, banned_json(row))
            # новый пароль и новый ключ игрока: другие устройства выйдут из аккаунта
            _db.execute("UPDATE accounts SET pass_hash=? WHERE email=?", (pass_hash, email))
            _db.execute("UPDATE players SET token=? WHERE id=?", (secrets.token_hex(16), row["id"]))
            _db.execute("DELETE FROM reset_codes WHERE email=?", (email,))
            _db.commit()
            row = _db.execute("SELECT * FROM players WHERE id=?", (row["id"],)).fetchone()
        print(f"{time.strftime('%H:%M:%S')}  пароль сброшен по коду — ID {row['id']}")
        return self.send_json(200, login_json(row, email))

    # ---- страница для проверки в браузере
    def send_page(self):
        now = time.time()
        with _db_lock:
            rows = _db.execute("SELECT * FROM players ORDER BY last_seen DESC LIMIT 200").fetchall()
            total = _db.execute("SELECT COUNT(*) FROM players").fetchone()[0]
        if not PUBLIC_LIST:
            rows_shown = []                                    # в интернете — только счётчики
        else:
            rows_shown = rows
        lines = []
        for r in rows_shown:
            p = player_json(r, now)
            state = "в сети" if p["online"] else f"был {p['lastSeenSec'] // 60} мин назад"
            lines.append(f"<tr><td>{r['id']}</td><td>{html.escape(nick(r))}</td><td>{state}</td>"
                         f"<td>{r['level']}</td><td>{r['zoo']}/{r['zoo_total']}</td><td>{r['score']}</td><td>{r['gems']}</td></tr>")
        online = sum(1 for r in rows if player_json(r, now)["online"])
        table = ("<table><tr><th>ID</th><th>Имя</th><th>Статус</th><th>Уровень</th><th>Животные</th>"
                 "<th>Монеты</th><th>Кристаллы</th></tr>" + "".join(lines) + "</table>") if PUBLIC_LIST else ""
        page = f"""<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Сервер HatchMerge</title>
<style>body{{font-family:system-ui,sans-serif;margin:24px;background:#fff3e0;color:#3b2a4a}}
table{{border-collapse:collapse;background:#fff}}td,th{{padding:6px 12px;border-bottom:1px solid #eee;text-align:left}}</style>
<h2>Сервер «Яйца-сюрпризы» работает</h2>
<p>Игроков: {total} · сейчас в сети: {online}</p>
{table}
<p><a href="/admin">Вход для администратора</a></p>"""
        data = page.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    # ================================================================ страница администратора
    def send_html(self, code, page, headers=()):
        data = page.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Referrer-Policy", "no-referrer")
        for k, v in headers:
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(data)

    def redirect(self, where, headers=()):
        self.send_response(303)
        self.send_header("Location", where)
        self.send_header("Content-Length", "0")
        for k, v in headers:
            self.send_header(k, v)
        self.end_headers()

    def session_cookie(self):
        for part in self.headers.get("Cookie", "").split(";"):
            k, _, v = part.strip().partition("=")
            if k == "hm_admin":
                return v
        return ""

    def admin_ok(self):
        if not ADMIN_PASSWORD:
            return not BEHIND_PROXY           # без пароля — только на своём ПК
        sess = admin_sessions.get(self.session_cookie())
        return sess is not None and sess["exp"] > time.time()

    def admin_csrf(self):
        if not ADMIN_PASSWORD:
            return LOCAL_CSRF
        sess = admin_sessions.get(self.session_cookie())
        return sess["csrf"] if sess else ""

    def admin_login_page(self, error=""):
        if not ADMIN_PASSWORD and BEHIND_PROXY:
            msg = "Страница администратора выключена: на сервере не задан пароль (HATCHMERGE_ADMIN_PASSWORD)."
            return self.send_html(403, admin_shell("Администратор", f"<p class=card>{msg}</p>"))
        err = f"<p class=err>{html.escape(error)}</p>" if error else ""
        body = f"""<form class="card login" method="post" action="/admin/login">
<h2>Вход для администратора</h2>{err}
<input type="password" name="password" placeholder="Пароль" autofocus required>
<button>Войти</button></form>"""
        return self.send_html(200, admin_shell("Вход", body))

    def admin_login(self):
        length = clamp_int(self.headers.get("Content-Length"), 0, 4096, 0)
        form = parse_qs(self.rfile.read(length).decode("utf-8", "replace"))
        password = (form.get("password") or [""])[0]
        if not ADMIN_PASSWORD:
            return self.redirect("/admin")
        if not admin_login_limit.allow(self.client_ip()):
            return self.admin_login_page("Слишком много попыток. Подождите 15 минут.")
        if not secrets.compare_digest(password.encode("utf-8"), ADMIN_PASSWORD.encode("utf-8")):
            time.sleep(1)
            return self.admin_login_page("Неверный пароль")
        sid = secrets.token_urlsafe(32)
        now = time.time()
        for k in [k for k, v in admin_sessions.items() if v["exp"] < now]:
            admin_sessions.pop(k, None)
        admin_sessions[sid] = {"exp": now + ADMIN_SESSION_HOURS * 3600, "csrf": secrets.token_urlsafe(16)}
        secure = "; Secure" if BEHIND_PROXY else ""
        cookie = f"hm_admin={sid}; Path=/; HttpOnly; SameSite=Strict; Max-Age={ADMIN_SESSION_HOURS * 3600}{secure}"
        print(f"{time.strftime('%H:%M:%S')}  вход администратора с {self.client_ip()}")
        return self.redirect("/admin", [("Set-Cookie", cookie)])

    def admin_logout(self):
        admin_sessions.pop(self.session_cookie(), None)
        return self.redirect("/admin", [("Set-Cookie", "hm_admin=; Path=/; Max-Age=0")])

    def admin_list(self):
        now = time.time()
        q = parse_qs(self.path.partition("?")[2]).get("q", [""])[0].strip().lower()[:100]
        with _db_lock:
            rows = _db.execute("""SELECT p.*, a.email FROM players p LEFT JOIN accounts a ON a.player_id = p.id
                                  ORDER BY p.last_seen DESC""").fetchall()
        total, accounts = len(rows), sum(1 for r in rows if r["email"])
        banned = sum(1 for r in rows if r["banned"])
        online = sum(1 for r in rows if player_json(r, now)["online"])
        today = sum(1 for r in rows if now - r["created"] < 86400)
        if q:
            rows = [r for r in rows if q in r["id"] or q in (r["email"] or "") or q in nick(r).lower()]

        lines = []
        for r in rows[:500]:
            p = player_json(r, now)
            status = "<span class=on>● в сети</span>" if p["online"] else f"<span class=off>● {ago(p['lastSeenSec'])}</span>"
            if r["banned"]:
                status = "<span class=ban>⛔ заблокирован</span>"
            email = html.escape(r["email"]) if r["email"] else "<span class=muted>гость</span>"
            lines.append(
                f"<tr onclick=\"location='/admin/player/{r['id']}'\"><td><b>{r['id']}</b></td>"
                f"<td>{html.escape(nick(r))}</td><td>{email}</td><td>{status}</td>"
                f"<td class=n>{r['level']}</td><td class=n>{r['zoo']}/{r['zoo_total']}</td>"
                f"<td class=n>{r['score']:,}</td><td class=n>{r['gems']}</td><td class=n>{r['best_egg']}</td>"
                f"<td class=n>{r['save_ver'] or '—'}</td><td>{fmt_time(r['created'])}</td></tr>".replace(f"{r['score']:,}", f"{r['score']:,}".replace(",", " ")))
        more = f"<p class=muted>Показаны первые 500 из {len(rows)}</p>" if len(rows) > 500 else ""
        body = f"""<div class=stats>
<div class=card><b>{total}</b>игроков</div><div class=card><b>{online}</b>сейчас в сети</div>
<div class=card><b>{accounts}</b>с аккаунтом</div><div class=card><b>{today}</b>новых за сутки</div>
<div class=card><b>{banned}</b>заблокировано</div></div>
<form class=search><input name=q value="{html.escape(q)}" placeholder="Поиск: ID, почта или имя"><button>Найти</button>
{'<a href="/admin">сбросить</a>' if q else ''}</form>
<div class=wrap><table><tr><th>ID</th><th>Имя</th><th>Почта</th><th>Статус</th><th>Уровень</th><th>Животные</th>
<th>Монеты</th><th>Кристаллы</th><th>Лучшее яйцо</th><th>Версия сохр.</th><th>Создан</th></tr>
{''.join(lines) or '<tr><td colspan=11 class=muted>Никого не найдено</td></tr>'}</table></div>{more}
<p class=muted>Страница обновляется сама каждые 20 секунд. Нажмите на строку, чтобы открыть игрока.</p>"""
        return self.send_html(200, admin_shell("Игроки", body, refresh=20))

    def admin_player(self, pid):
        now = time.time()
        with _db_lock:
            r = _db.execute("""SELECT p.*, a.email, a.created AS acc_created FROM players p
                               LEFT JOIN accounts a ON a.player_id = p.id WHERE p.id=?""", (pid,)).fetchone()
        if r is None:
            return self.send_html(404, admin_shell("Нет игрока", "<p class=card>Игрок не найден. <a href='/admin'>К списку</a></p>"))
        p = player_json(r, now)
        try:
            save = json.loads(r["save"]) if r["save"] else {}
        except ValueError:
            save = {}

        def row(k, v):
            return f"<tr><th>{k}</th><td>{v}</td></tr>"

        status = "<span class=on>● в сети</span>" if p["online"] else f"<span class=off>● не в сети, {ago(p['lastSeenSec'])}</span>"
        if r["banned"]:
            status = "<span class=ban>⛔ заблокирован</span>"
        info = "".join([
            row("ID", f"<b>{r['id']}</b>"), row("Имя", html.escape(nick(r))),
            row("Почта", html.escape(r["email"]) if r["email"] else "гость (без аккаунта)"),
            row("Статус", status), row("Последний раз в сети", fmt_time(r["last_seen"])),
            row("Создан", fmt_time(r["created"])),
            row("Аккаунт создан", fmt_time(r["acc_created"]) if r["acc_created"] else "—"),
            row("Уровень", r["level"]), row("Животных открыто", f"{r['zoo']} из {r['zoo_total']}"),
            row("Монеты", f"{r['score']:,}".replace(",", " ")), row("Кристаллы", r["gems"]),
            row("Лучшее яйцо на поле", r["best_egg"]),
        ])

        zoo = "".join(f"<li class={'got' if i < r['zoo'] else 'lock'}>{html.escape(name)}</li>"
                      for i, name in enumerate(ZOO_NAMES))
        if save:
            grid = save.get("grid") or []
            cells = ""
            for i in range(25):
                lvl = grid[i] if i < len(grid) and isinstance(grid[i], int) else 0
                color = EGG_COLORS[lvl] if 1 <= lvl <= 7 else "transparent"
                cells += f"<div class=cell style='background:{color}'>{lvl or ''}</div>"
            onoff = lambda v: "вкл" if v else "выкл"
            save_html = "".join([
                row("Версия сохранения", r["save_ver"]),
                row("Энергия", f"{save.get('energy', '—')} / 10"),
                row("Музыка / звуки", f"{onoff(save.get('music'))} / {onoff(save.get('sfx'))}"),
            ]) + f"<tr><th>Поле</th><td><div class=grid>{cells}</div></td></tr>"
        else:
            save_html = row("Сохранение", "нет — игрок не входил в аккаунт")

        body = f"""<p><a href="/admin">← ко всем игрокам</a></p>{self.flash()}
<div class=cols><div class=card><h3>Игрок</h3><table class=kv>{info}</table></div>
<div class=card><h3>Сохранение на сервере</h3><table class=kv>{save_html}</table>
<h3>Зоопарк</h3><ul class=zoo>{zoo}</ul></div></div>
{self.admin_manage(r)}"""
        return self.send_html(200, admin_shell(f"Игрок {r['id']}", body))

    def flash(self):
        msg = parse_qs(self.path.partition("?")[2]).get("msg", [""])[0][:200]
        secret = self.session_store().pop("secret", "")
        out = f"<p class='card flash'>{html.escape(msg)}</p>" if msg else ""
        if secret:
            out += f"<p class='card flash secret'>{secret}</p>"
        return out

    def session_store(self):
        if not ADMIN_PASSWORD:
            return LOCAL_STORE
        return admin_sessions.get(self.session_cookie(), {})

    def admin_manage(self, r):
        pid, csrf = r["id"], html.escape(self.admin_csrf())
        hidden = f"<input type=hidden name=csrf value='{csrf}'>"
        with _db_lock:
            grants = _db.execute("SELECT * FROM grants WHERE player_id=? ORDER BY id DESC LIMIT 20", (pid,)).fetchall()
            log = _db.execute("SELECT * FROM admin_log WHERE player_id=? ORDER BY id DESC LIMIT 20", (pid,)).fetchall()

        if r["banned"]:
            ban = f"""<p><span class=ban>⛔ Заблокирован {fmt_time(r['banned_at'])}</span><br>
Причина: {html.escape(r['ban_reason']) or '—'}</p>
<form method=post action="/admin/player/{pid}/unban">{hidden}<button class=ok>Разблокировать</button></form>"""
        else:
            ban = f"""<p class=muted>Игра покажет экран блокировки, вход в аккаунт и поиск игрока перестанут работать.</p>
<form method=post action="/admin/player/{pid}/ban" class=row>{hidden}
<input name=reason maxlength=200 placeholder="Причина (её увидит игрок)">
<button onclick="return confirm('Заблокировать игрока {pid}?')">Заблокировать</button></form>"""

        if r["email"]:
            ban += f"""<h3>Пароль</h3>
<p class=muted>Создать временный пароль для {html.escape(r['email'])}. Он появится здесь один раз — передайте его родителю.
Игрок на всех устройствах выйдет из аккаунта и войдёт с новым паролем.</p>
<form method=post action="/admin/player/{pid}/resetpw">{hidden}
<button onclick="return confirm('Сбросить пароль игрока {pid}?')">Сбросить пароль</button></form>"""

        def signed(v, unit):
            return f"<span class={'plus' if v > 0 else 'minus'}>{'+' if v > 0 else '−'}{abs(v):,} {unit}</span>".replace(",", " ") if v else ""

        g_rows = []
        for g in grants:
            what = " ".join(x for x in (signed(g["coins"], "монет"), signed(g["gems"], "крист."), signed(g["energy"], "энерг.")) if x)
            if g["canceled"]:
                st = "<span class=muted>отменено</span>"
            elif g["applied_at"]:
                st = f"<span class=on>получено {fmt_time(g['applied_at'])}</span>"
            else:
                st = (f"<span class=off>ждёт игрока</span> <form method=post action='/admin/grant/{g['id']}/cancel' "
                      f"style='display:inline'>{hidden}<button class=small>Отменить</button></form>")
            g_rows.append(f"<tr><td>{fmt_time(g['created'])}</td><td>{what}</td><td>{html.escape(g['note'])}</td><td>{st}</td></tr>")
        g_table = (f"<table><tr><th>Когда</th><th>Изменение</th><th>Комментарий</th><th>Статус</th></tr>{''.join(g_rows)}</table>"
                   if g_rows else "<p class=muted>Изменений ещё не было</p>")

        l_rows = "".join(f"<tr><td>{fmt_time(x['ts'])}</td><td>{html.escape(x['action'])}</td>"
                         f"<td>{html.escape(x['details'])}</td><td class=muted>{html.escape(x['ip'])}</td></tr>" for x in log)
        l_table = (f"<table><tr><th>Когда</th><th>Действие</th><th>Подробности</th><th>IP</th></tr>{l_rows}</table>"
                   if l_rows else "<p class=muted>Пусто</p>")

        return f"""<div class=cols style="margin-top:16px">
<div class=card><h3>Ресурсы</h3>
<p class=muted>Плюс — начислить, минус — списать. Игра применит изменение при следующей связи с сервером
(обычно до 20 секунд; если игрок не в сети — когда он вернётся). Ниже нуля ресурсы не опустятся.</p>
<form method=post action="/admin/player/{pid}/grant" class=grantf>{hidden}
<label>Монеты<input type=number name=coins value=0 min=-1000000 max=1000000></label>
<label>Кристаллы<input type=number name=gems value=0 min=-10000 max=10000></label>
<label>Энергия<input type=number name=energy value=0 min=-99 max=99></label>
<label class=wide>Комментарий (только для вас)<input name=note maxlength=100 placeholder="например: компенсация"></label>
<button>Применить</button></form>
<h3>История изменений</h3><div class=wrap2>{g_table}</div></div>
<div class=card><h3>Блокировка</h3>{ban}
<h3>Журнал администратора</h3><div class=wrap2>{l_table}</div></div></div>"""

    def admin_action(self, path):
        if not self.admin_ok():
            return self.redirect("/admin")
        length = clamp_int(self.headers.get("Content-Length"), 0, 8192, 0)
        form = {k: v[0] for k, v in parse_qs(self.rfile.read(length).decode("utf-8", "replace")).items()}
        if not secrets.compare_digest(form.get("csrf", "").encode(), self.admin_csrf().encode()):
            return self.send_html(403, admin_shell("Ошибка", "<p class=card>Форма устарела — обновите страницу.</p>"))
        ip, now = self.client_ip(), time.time()

        m = re.fullmatch(r"/admin/grant/(\d+)/cancel", path)
        if m:
            with _db_lock:
                g = _db.execute("SELECT * FROM grants WHERE id=?", (int(m.group(1)),)).fetchone()
                if g is None:
                    return self.redirect("/admin")
                if g["applied_at"] is None and not g["canceled"]:
                    _db.execute("UPDATE grants SET canceled=1 WHERE id=?", (g["id"],))
                    admin_log(ip, g["player_id"], "Отмена изменения", f"№{g['id']}")
                    msg = "Изменение отменено"
                else:
                    msg = "Уже получено игроком — отменить нельзя"
                _db.commit()
            return self.redirect(f"/admin/player/{g['player_id']}?msg={quote(msg)}")

        m = re.fullmatch(r"/admin/player/(\d{6})/(grant|ban|unban|resetpw)", path)
        if not m:
            return self.redirect("/admin")
        pid, action = m.groups()
        with _db_lock:
            r = _db.execute("SELECT * FROM players WHERE id=?", (pid,)).fetchone()
            if r is None:
                return self.redirect("/admin")
            if action == "grant":
                coins = clamp_int(form.get("coins"), -1_000_000, 1_000_000, 0)
                gems = clamp_int(form.get("gems"), -10_000, 10_000, 0)
                energy = clamp_int(form.get("energy"), -99, 99, 0)
                note = form.get("note", "").strip()[:100]
                if not (coins or gems or energy):
                    msg = "Ничего не изменено: все значения равны нулю"
                else:
                    _db.execute("INSERT INTO grants (player_id, coins, gems, energy, note, created) VALUES (?,?,?,?,?,?)",
                                (pid, coins, gems, energy, note, now))
                    admin_log(ip, pid, "Ресурсы", f"монеты {coins:+}, кристаллы {gems:+}, энергия {energy:+}"
                                                  + (f" ({note})" if note else ""))
                    msg = "Изменение отправлено — игра применит его при следующей связи с сервером"
            elif action == "ban":
                reason = form.get("reason", "").strip()[:200]
                _db.execute("UPDATE players SET banned=1, ban_reason=?, banned_at=?, online=0 WHERE id=?", (reason, now, pid))
                admin_log(ip, pid, "Блокировка", reason)
                msg = "Игрок заблокирован"
            elif action == "resetpw":
                acc = _db.execute("SELECT email FROM accounts WHERE player_id=?", (pid,)).fetchone()
                if acc is None:
                    msg = "У игрока нет аккаунта — пароль сбрасывать нечего"
                else:
                    pw = temp_password()
                    _db.execute("UPDATE accounts SET pass_hash=? WHERE player_id=?", (hash_password(pw), pid))
                    _db.execute("UPDATE players SET token=? WHERE id=?", (secrets.token_hex(16), pid))
                    admin_log(ip, pid, "Сброс пароля", acc["email"])
                    msg = "Пароль сброшен"
                    self.session_store()["secret"] = (f"Временный пароль для {html.escape(acc['email'])}: "
                                                      f"<b class=pw>{pw}</b> — сейчас он показан в последний раз")
            else:
                _db.execute("UPDATE players SET banned=0, ban_reason='', banned_at=0 WHERE id=?", (pid,))
                admin_log(ip, pid, "Разблокировка", "")
                msg = "Игрок разблокирован"
            _db.commit()
        print(f"{time.strftime('%H:%M:%S')}  администратор: {msg.lower()} — ID {pid}")
        return self.redirect(f"/admin/player/{pid}?msg={quote(msg)}")


# ---------------------------------------------------------------- автопоиск в сети


def discovery_loop():
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(("", DISCOVERY_PORT))
    except OSError as e:
        print(f"Автопоиск в сети не запущен (порт {DISCOVERY_PORT}): {e}")
        return
    reply = f"HATCHMERGE_SERVER {HTTP_PORT}".encode()
    while True:
        try:
            data, addr = sock.recvfrom(256)
            if data.startswith(b"HATCHMERGE_DISCOVER"):
                sock.sendto(reply, addr)
        except OSError:
            time.sleep(0.5)


def lan_addresses():
    found = []
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))          # пакеты не отправляются, так узнаём основной адрес
        found.append(s.getsockname()[0])
        s.close()
    except OSError:
        pass
    try:
        for ip in socket.gethostbyname_ex(socket.gethostname())[2]:
            if ip not in found and not ip.startswith("127."):
                found.append(ip)
    except OSError:
        pass
    return found


def main():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    if DISCOVERY:
        threading.Thread(target=discovery_loop, daemon=True).start()
    httpd = ThreadingHTTPServer((HTTP_HOST, HTTP_PORT), Handler)
    httpd.daemon_threads = True

    print("=" * 56)
    print(" Сервер игроков «Яйца-сюрпризы» запущен")
    print(f" Этот ПК:          http://127.0.0.1:{HTTP_PORT}")
    if HTTP_HOST != "127.0.0.1":
        for ip in lan_addresses():
            print(f" Для телефонов:    http://{ip}:{HTTP_PORT}")
    print(f" База игроков:     {DB_PATH}")
    print(" Остановить: Ctrl+C или закрыть это окно")
    print("=" * 56)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nСервер остановлен")


if __name__ == "__main__":
    main()
