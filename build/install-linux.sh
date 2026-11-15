#!/usr/bin/env bash
# Установка и обновление SunsetMP Server как systemd-сервиса.
#
# Первая установка (файлы сервера уже лежат в /opt/sunsetmp-server):
#   sudo ./install-linux.sh --music /srv/music
#
# Установка/обновление из архива (его заливает Deploy-Server.ps1):
#   sudo ./install-linux.sh --archive /tmp/sunsetmp-server.tar.gz [--music /srv/music]
#
# Параметры:
#   --music <папка>    папка с музыкой (обязательно при первой установке)
#   --token <токен>    токен доступа; если не задан при первой установке — будет сгенерирован
#   --port  <порт>     порт сервера (по умолчанию 5080)
#   --archive <файл>   .tar.gz с опубликованным сервером — распаковать в --dir, заменив старые файлы
#   --dir <папка>      куда установлен сервер (по умолчанию /opt/sunsetmp-server)
#   --uninstall        остановить и удалить сервис (файлы и настройки остаются)
#
# Настройки (папка с музыкой, токен, порт) хранятся в /etc/sunsetmp-server.env и переопределяют
# appsettings.json — поэтому обновление файлов сервера их не затирает.

set -euo pipefail

SERVICE=sunsetmp-server
SVC_USER=sunsetmp
APP_DIR=/opt/sunsetmp-server
ENV_FILE=/etc/sunsetmp-server.env
UNIT=/etc/systemd/system/${SERVICE}.service

MUSIC=""; TOKEN=""; PORT=""; ARCHIVE=""; UNINSTALL=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --music)   MUSIC="${2:?}";   shift 2 ;;
    --token)   TOKEN="${2:?}";   shift 2 ;;
    --port)    PORT="${2:?}";    shift 2 ;;
    --archive) ARCHIVE="${2:?}"; shift 2 ;;
    --dir)     APP_DIR="${2:?}"; shift 2 ;;
    --uninstall) UNINSTALL=1;    shift ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "Неизвестный параметр: $1" >&2; exit 1 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "Запустите от root: sudo $0 ..." >&2
  exit 1
fi

# ---------- удаление ----------
if [[ $UNINSTALL -eq 1 ]]; then
  systemctl disable --now "$SERVICE" 2>/dev/null || true
  rm -f "$UNIT"
  systemctl daemon-reload
  echo "Сервис удалён. Файлы ($APP_DIR) и настройки ($ENV_FILE) оставлены."
  exit 0
fi

# ---------- файлы сервера ----------
if [[ -n "$ARCHIVE" ]]; then
  [[ -f "$ARCHIVE" ]] || { echo "Архив не найден: $ARCHIVE" >&2; exit 1; }
  # защита от rm -rf в корне или в первом уровне
  [[ "$APP_DIR" =~ ^/[^/]+/[^/]+ ]] || { echo "Подозрительная папка установки: $APP_DIR" >&2; exit 1; }

  echo "==> Останавливаю сервис и заменяю файлы в $APP_DIR"
  systemctl stop "$SERVICE" 2>/dev/null || true
  mkdir -p "$APP_DIR"
  find "$APP_DIR" -mindepth 1 -delete
  tar -xzf "$ARCHIVE" -C "$APP_DIR"
  rm -f "$ARCHIVE"
fi

[[ -f "$APP_DIR/SunsetMP.Server.dll" ]] || { echo "В $APP_DIR нет SunsetMP.Server.dll" >&2; exit 1; }

# ---------- пользователь ----------
if ! id "$SVC_USER" &>/dev/null; then
  echo "==> Создаю системного пользователя $SVC_USER"
  useradd --system --no-create-home --shell /usr/sbin/nologin "$SVC_USER"
fi

# ---------- настройки ----------
touch "$ENV_FILE"

set_env() {   # set_env КЛЮЧ ЗНАЧЕНИЕ — заменить или добавить строку
  grep -v "^$1=" "$ENV_FILE" > "$ENV_FILE.tmp" || true
  echo "$1=$2" >> "$ENV_FILE.tmp"
  mv "$ENV_FILE.tmp" "$ENV_FILE"
}
get_env() { grep "^$1=" "$ENV_FILE" | head -n1 | cut -d= -f2- || true; }

GENERATED_TOKEN=""
[[ -n "$MUSIC" ]] && set_env Library__MusicPath "$MUSIC"
[[ -n "$TOKEN" ]] && set_env Auth__Token "$TOKEN"
[[ -n "$PORT"  ]] && set_env Urls "http://0.0.0.0:$PORT"

if [[ -z "$(get_env Library__MusicPath)" ]]; then
  echo "Не задана папка с музыкой. Запустите с --music /путь/к/музыке" >&2
  exit 1
fi
if [[ -z "$(get_env Auth__Token)" ]]; then
  GENERATED_TOKEN="$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')"
  set_env Auth__Token "$GENERATED_TOKEN"
fi
[[ -z "$(get_env Urls)" ]] && set_env Urls "http://0.0.0.0:5080"

chown root:"$SVC_USER" "$ENV_FILE"
chmod 640 "$ENV_FILE"          # токен не должен быть доступен всем

MUSIC_PATH="$(get_env Library__MusicPath)"
if ! runuser -u "$SVC_USER" -- test -r "$MUSIC_PATH" -a -x "$MUSIC_PATH"; then
  echo "ВНИМАНИЕ: пользователь $SVC_USER не может читать '$MUSIC_PATH'." >&2
  echo "  Дайте доступ, например:  setfacl -R -m u:$SVC_USER:rX '$MUSIC_PATH'" >&2
fi

# ---------- права на файлы сервера ----------
chown -R root:"$SVC_USER" "$APP_DIR"
chmod -R u=rwX,g=rX,o= "$APP_DIR"
[[ -f "$APP_DIR/SunsetMP.Server" ]] && chmod 750 "$APP_DIR/SunsetMP.Server"

# ---------- как запускать: self-contained или нужен установленный dotnet ----------
if grep -q '"includedFrameworks"' "$APP_DIR/SunsetMP.Server.runtimeconfig.json" 2>/dev/null; then
  EXEC="$APP_DIR/SunsetMP.Server"
else
  DOTNET="$(command -v dotnet || true)"
  if [[ -z "$DOTNET" ]]; then
    echo "Не найден dotnet. Установите ASP.NET Core Runtime 10 (например: apt install aspnetcore-runtime-10.0)" >&2
    echo "или публикуйте сервер с --self-contained." >&2
    exit 1
  fi
  EXEC="$DOTNET $APP_DIR/SunsetMP.Server.dll"
fi

# ---------- systemd ----------
echo "==> Записываю $UNIT"
cat > "$UNIT" <<UNITEOF
[Unit]
Description=SunsetMP Music Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$SVC_USER
Group=$SVC_USER
WorkingDirectory=$APP_DIR
EnvironmentFile=-$ENV_FILE
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1
ExecStart=$EXEC
Restart=always
RestartSec=5
TimeoutStopSec=15
SyslogIdentifier=$SERVICE
LimitNOFILE=65535

# ограничения доступа
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=read-only

[Install]
WantedBy=multi-user.target
UNITEOF

systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null
systemctl restart "$SERVICE"

# ---------- проверка ----------
URL_PORT="$(get_env Urls | grep -oE ':[0-9]+' | tail -n1 | tr -d ':')"
URL_PORT="${URL_PORT:-5080}"
sleep 2

if ! systemctl is-active --quiet "$SERVICE"; then
  echo "Сервис не запустился. Последние сообщения:" >&2
  journalctl -u "$SERVICE" -n 25 --no-pager >&2
  exit 1
fi
echo "==> Сервис запущен (порт $URL_PORT)"

if command -v curl >/dev/null; then
  for _ in 1 2 3 4 5 6 7 8; do
    if RESP="$(curl -fsS -m 3 -H "Authorization: Bearer $(get_env Auth__Token)" "http://127.0.0.1:$URL_PORT/api/ping" 2>/dev/null)"; then
      echo "==> /api/ping: $RESP"
      break
    fi
    sleep 1
  done
fi

if [[ -n "$GENERATED_TOKEN" ]]; then
  echo
  echo "Сгенерирован токен доступа (введите его в приложении):"
  echo "  $GENERATED_TOKEN"
  echo "Он сохранён в $ENV_FILE"
fi

WATCHES="$(cat /proc/sys/fs/inotify/max_user_watches 2>/dev/null || echo 0)"
if [[ "$WATCHES" -lt 100000 ]]; then
  echo
  echo "Подсказка: для слежения за большой библиотекой увеличьте лимит inotify:"
  echo "  echo 'fs.inotify.max_user_watches=524288' | sudo tee /etc/sysctl.d/99-sunsetmp.conf && sudo sysctl --system"
fi

echo
echo "Логи:    journalctl -u $SERVICE -f"
echo "Статус:  systemctl status $SERVICE"
