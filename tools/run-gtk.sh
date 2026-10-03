#!/usr/bin/env bash
set -euo pipefail

# ChBrowser Linux (GTK4 / WebKitGTK 6.0) GUI 起動スクリプト (Docker -> ホスト X11 画面)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

export DOCKER_CONFIG=/tmp/.docker
TARGET_DISPLAY="${DISPLAY:-:1}"

echo "=== ChBrowser Linux (GTK4 / WebKitGTK 6.0) 起動スクリプト ==="
echo "ホスト DISPLAY: $TARGET_DISPLAY"

# 1. ホスト側での X11 接続許可
if command -v xhost >/dev/null 2>&1; then
  echo "X11 アクセス権限をローカルコンテナに許可中 (xhost +local:root)..."
  xhost +local:root >/dev/null 2>&1 || xhost +local: >/dev/null 2>&1 || true
fi

# 2. Xauthority ファイルの探索と設定
XAUTH_ARGS=()
AUTH_FILE=""
if [ -n "${XAUTHORITY:-}" ] && [ -f "$XAUTHORITY" ]; then
  AUTH_FILE="$XAUTHORITY"
elif [ -f "$HOME/.Xauthority" ]; then
  AUTH_FILE="$HOME/.Xauthority"
elif [ -f "/run/user/$(id -u)/gdm/Xauthority" ]; then
  AUTH_FILE="/run/user/$(id -u)/gdm/Xauthority"
fi

if [ -n "$AUTH_FILE" ]; then
  echo "Xauthority: $AUTH_FILE"
  XAUTH_ARGS=(
    -v "$AUTH_FILE:/root/.Xauthority:ro"
    -e XAUTHORITY=/root/.Xauthority
  )
fi

# 3. DBus セッションバスソケットの探索とマウント (fcitx5 / ibus 通信用)
DBUS_ARGS=()
if [ -n "${DBUS_SESSION_BUS_ADDRESS:-}" ]; then
  DBUS_SOCKET_PATH="${DBUS_SESSION_BUS_ADDRESS#unix:path=}"
  if [ -S "$DBUS_SOCKET_PATH" ]; then
    DBUS_ARGS=(
      -v "$DBUS_SOCKET_PATH:$DBUS_SOCKET_PATH"
      -e DBUS_SESSION_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS"
    )
  fi
elif [ -S "/run/user/$(id -u)/bus" ]; then
  DBUS_ARGS=(
    -v "/run/user/$(id -u)/bus:/run/user/$(id -u)/bus"
    -e DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$(id -u)/bus"
  )
fi

# 4. GTK4 IME モジュール設定 (fcitx5 の場合は GTK_IM_MODULE=fcitx5)
CONTAINER_GTK_IM="${GTK_IM_MODULE:-}"
if [ "$CONTAINER_GTK_IM" = "fcitx" ]; then
  CONTAINER_GTK_IM="fcitx5"
elif [ -z "$CONTAINER_GTK_IM" ] && [[ "${XMODIFIERS:-}" == *"@im=fcitx"* ]]; then
  CONTAINER_GTK_IM="fcitx5"
fi

# GPU デバイスの有無を確認
DRI_ARGS=()
if [ -d /dev/dri ]; then
  DRI_ARGS=(--device /dev/dri:/dev/dri)
fi

echo "コンテナを起動して ChBrowser.Gtk を実行します..."

docker run --rm -it \
  --net=host \
  --ipc=host \
  -v /tmp/.X11-unix:/tmp/.X11-unix \
  "${XAUTH_ARGS[@]}" \
  "${DRI_ARGS[@]}" \
  "${DBUS_ARGS[@]}" \
  -e DISPLAY="$TARGET_DISPLAY" \
  -e GTK_IM_MODULE="$CONTAINER_GTK_IM" \
  -e QT_IM_MODULE="${QT_IM_MODULE:-}" \
  -e XMODIFIERS="${XMODIFIERS:-}" \
  -e WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1 \
  -e NO_AT_BRIDGE=1 \
  -e NO_PROXY="127.0.0.1,localhost,::1" \
  -e no_proxy="127.0.0.1,localhost,::1" \
  -e CHBROWSER_EDDI_BASE_URL="${CHBROWSER_EDDI_BASE_URL:-}" \
  -e CHBROWSER_DATA_DIR="${CHBROWSER_DATA_DIR:-}" \
  -v "$REPO_ROOT:/work" \
  -w /work \
  chbrowser-gtk:latest \
  dotnet run --project src/ChBrowser.Gtk/ChBrowser.Gtk.csproj "$@"
