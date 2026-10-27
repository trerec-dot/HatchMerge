#!/usr/bin/env bash
# Ежедневная копия базы игроков. Хранятся последние 14 дней.
set -euo pipefail
DB=/var/lib/hatchmerge/hatchmerge.db
DIR=/var/backups/hatchmerge
mkdir -p "$DIR"
[[ -f "$DB" ]] || exit 0
OUT="$DIR/hatchmerge-$(date +%F).db"
python3 - "$DB" "$OUT" <<'PY'
import sqlite3, sys
src, dst = sqlite3.connect(sys.argv[1]), sqlite3.connect(sys.argv[2])
src.backup(dst)          # безопасная копия даже во время работы сервера
dst.close(); src.close()
PY
gzip -f "$OUT"
find "$DIR" -name 'hatchmerge-*.db.gz' -mtime +14 -delete
