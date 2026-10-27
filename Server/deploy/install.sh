#!/usr/bin/env bash
# Установка сервера игроков «Яйца-сюрпризы» на VPS (Ubuntu 22.04 / 24.04, Debian 12).
#
#   sudo bash install.sh game.example.com you@mail.com — со своим доменом и почтой (рекомендуется)
#   sudo bash install.sh game.example.com     — со своим доменом
#   sudo bash install.sh                      — без домена: адрес вида 203-0-113-5.sslip.io
#
# Что делает: ставит Python и Caddy, запускает сервер как службу (автозапуск, перезапуск при сбое),
# включает HTTPS-сертификат, открывает в брандмауэре только SSH, 80 и 443,
# настраивает ежедневную резервную копию базы (хранится 14 дней).
# Повторный запуск безопасен — так же обновляется код сервера.
set -euo pipefail

if [[ $EUID -ne 0 ]]; then echo "Запустите от root: sudo bash install.sh [домен]"; exit 1; fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$HERE/../hatchmerge_server.py"
[[ -f "$SRC" ]] || { echo "Не найден файл сервера: $SRC"; exit 1; }

export DEBIAN_FRONTEND=noninteractive
echo "==> Устанавливаю пакеты"
apt-get update -y
apt-get install -y python3 curl ufw gnupg apt-transport-https ca-certificates

PUBLIC_IP="$(curl -4 -fsS --max-time 10 https://api.ipify.org || hostname -I | awk '{print $1}')"
DOMAIN="${1:-}"
if [[ -n "$DOMAIN" && ! "$DOMAIN" =~ ^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$ ]]; then
  echo "!!  '$DOMAIN' не похоже на домен (нужен вид game.example.com) — устанавливаю без домена"
  DOMAIN=""
fi
EMAIL="${2:-}"   # с почтой Caddy может брать сертификат и у ZeroSSL, если у Let's Encrypt исчерпан лимит
if [[ -z "$DOMAIN" ]]; then
  DOMAIN="${PUBLIC_IP//./-}.sslip.io"
  echo "==> Домен не указан, использую $DOMAIN"
else
  RESOLVED="$(getent ahostsv4 "$DOMAIN" | awk 'NR==1{print $1}' || true)"
  if [[ "$RESOLVED" != "$PUBLIC_IP" ]]; then
    echo "!!  Домен $DOMAIN указывает на '${RESOLVED:-никуда}', а IP этого сервера $PUBLIC_IP."
    echo "    Создайте у регистратора A-запись $DOMAIN -> $PUBLIC_IP и подождите 5–30 минут."
    echo "    Установка продолжится, сертификат Caddy получит, когда запись заработает."
  fi
fi

if ! command -v caddy >/dev/null; then
  echo "==> Устанавливаю Caddy (официальный репозиторий)"
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
    | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
    > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -y
  apt-get install -y caddy
fi

echo "==> Копирую сервер"
id hatchmerge &>/dev/null || useradd --system --home /var/lib/hatchmerge --shell /usr/sbin/nologin hatchmerge
install -d -o hatchmerge -g hatchmerge -m 750 /var/lib/hatchmerge
install -d -m 755 /opt/hatchmerge
install -d -m 750 /var/backups/hatchmerge
install -m 644 "$SRC" /opt/hatchmerge/hatchmerge_server.py
install -m 755 "$HERE/backup.sh" /opt/hatchmerge/backup.sh

echo "==> Пароль страницы администратора (/admin)"
if ! grep -q '^HATCHMERGE_ADMIN_PASSWORD=.' /etc/hatchmerge.env 2>/dev/null; then
  echo "HATCHMERGE_ADMIN_PASSWORD=$(python3 -c 'import secrets; print(secrets.token_urlsafe(12))')" > /etc/hatchmerge.env
fi
if ! grep -q 'HATCHMERGE_SMTP_HOST' /etc/hatchmerge.env; then
  cat >> /etc/hatchmerge.env <<'ENV'
# Почта для писем с кодом сброса пароля. Уберите # в четырёх строках ниже, впишите свой ящик
# и пароль приложения, затем выполните: systemctl restart hatchmerge
#HATCHMERGE_SMTP_HOST=smtp.yandex.ru
#HATCHMERGE_SMTP_PORT=465
#HATCHMERGE_SMTP_USER=ваш_ящик@yandex.ru
#HATCHMERGE_SMTP_PASSWORD=пароль_приложения
ENV
fi
chmod 600 /etc/hatchmerge.env
ADMIN_PW="$(grep '^HATCHMERGE_ADMIN_PASSWORD=' /etc/hatchmerge.env | head -n 1 | cut -d= -f2-)"

cat > /etc/systemd/system/hatchmerge.service <<UNIT
[Unit]
Description=HatchMerge player server
After=network.target

[Service]
User=hatchmerge
Group=hatchmerge
WorkingDirectory=/var/lib/hatchmerge
Environment=HATCHMERGE_HOST=127.0.0.1
Environment=HATCHMERGE_PORT=8080
Environment=HATCHMERGE_DB=/var/lib/hatchmerge/hatchmerge.db
Environment=HATCHMERGE_PROXY=1
Environment=HATCHMERGE_DISCOVERY=0
Environment=HATCHMERGE_LIST=0
Environment=PYTHONUNBUFFERED=1
EnvironmentFile=/etc/hatchmerge.env
ExecStart=/usr/bin/python3 /opt/hatchmerge/hatchmerge_server.py
Restart=always
RestartSec=3
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=/var/lib/hatchmerge

[Install]
WantedBy=multi-user.target
UNIT

cat > /etc/systemd/system/hatchmerge-backup.service <<UNIT
[Unit]
Description=HatchMerge database backup

[Service]
Type=oneshot
ExecStart=/opt/hatchmerge/backup.sh
UNIT

cat > /etc/systemd/system/hatchmerge-backup.timer <<UNIT
[Unit]
Description=Daily HatchMerge database backup

[Timer]
OnCalendar=*-*-* 03:30:00
Persistent=true

[Install]
WantedBy=timers.target
UNIT

echo "==> Настраиваю HTTPS (Caddy) для $DOMAIN"
if [[ -f /etc/caddy/Caddyfile && ! -f /etc/caddy/Caddyfile.orig ]]; then cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.orig; fi
{
  if [[ -n "$EMAIL" ]]; then printf '{\n    email %s\n}\n\n' "$EMAIL"; fi
  cat <<CADDY
$DOMAIN {
    encode gzip
    reverse_proxy 127.0.0.1:8080
}
CADDY
} > /etc/caddy/Caddyfile

# На многих VPS заранее стоит nginx или apache и занимает порты 80/443 — тогда Caddy не запустится
for svc in nginx apache2 httpd; do
  if systemctl is-active --quiet "$svc" 2>/dev/null; then
    echo "==> Останавливаю $svc: он занимает порты 80/443, нужные для HTTPS"
    systemctl stop "$svc"
    systemctl disable "$svc" >/dev/null 2>&1 || true
  fi
done

echo "==> Брандмауэр: открываю SSH, 80 и 443"
SSH_PORT="$(echo "${SSH_CONNECTION:-}" | awk '{print $4}')"
ufw allow "${SSH_PORT:-22}/tcp" >/dev/null
ufw allow OpenSSH >/dev/null 2>&1 || true
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw --force enable >/dev/null

echo "==> Запускаю службы"
systemctl daemon-reload
systemctl enable hatchmerge >/dev/null 2>&1
systemctl restart hatchmerge
systemctl enable --now hatchmerge-backup.timer >/dev/null 2>&1
systemctl enable caddy >/dev/null 2>&1
systemctl restart caddy

sleep 2
if curl -fsS http://127.0.0.1:8080/api/ping >/dev/null; then
  echo "    Сервер игроков работает"
else
  echo "!!  Сервер не отвечает. Посмотрите журнал: journalctl -u hatchmerge -n 50"; exit 1
fi

echo "==> Жду HTTPS-сертификат (до 90 секунд)"
OK=0
for _ in $(seq 1 30); do
  if curl -fsS --max-time 5 "https://$DOMAIN/api/ping" >/dev/null 2>&1; then OK=1; break; fi
  sleep 3
done

echo
echo "========================================================"
if [[ $OK == 1 ]]; then
  echo " Готово! Сервер доступен из интернета:"
else
  echo " Сервер запущен, но HTTPS пока не отвечает."
  echo " Проверьте A-запись домена и порты 80/443 в панели хостинга,"
  echo " затем: journalctl -u caddy -n 50"
fi
echo
echo "   https://$DOMAIN"
echo
echo " Впишите эту строку в Assets/Resources/server_url.txt и пересоберите игру."
echo
echo " Страница администратора: https://$DOMAIN/admin"
echo " Пароль администратора:   $ADMIN_PW"
echo " (хранится в /etc/hatchmerge.env — там же его можно заменить, затем: systemctl restart hatchmerge)"
if grep -q '^HATCHMERGE_SMTP_HOST=' /etc/hatchmerge.env; then
  echo " Сброс пароля по почте: включён"
else
  echo " Сброс пароля по почте: выключен — настройте почту в /etc/hatchmerge.env (см. DEPLOY.md)"
fi
echo " Журнал сервера:   journalctl -u hatchmerge -f"
echo " Резервные копии:  /var/backups/hatchmerge (каждый день в 03:30)"
echo "========================================================"
