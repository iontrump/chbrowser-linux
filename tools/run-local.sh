#!/usr/bin/env bash
set -euo pipefail

# ChBrowser Linux (GTK4 / WebKitGTK 6.0) をコンテナを使わずホスト上で直接起動する。
# ホストの fcitx5 / Mozc にそのまま接続できるため、日本語入力が使える。
#
# 前提 (Ubuntu 24.04): libgtk-4-1 libadwaita-1-0 libwebkitgtk-6.0-4 fcitx5-frontend-gtk4 と .NET 8 SDK
# ビルド出力は .build-local/ に出す (src/*/obj, bin は Docker ビルドで root 所有になっていることがあるため)。
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS="$REPO_ROOT/.build-local"

# .NET 8 SDK を探す (~/.dotnet を優先。/usr/lib/dotnet が .NET 10 のみの環境があるため)
if [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
  export DOTNET_ROOT="$HOME/.dotnet"
else
  DOTNET="$(command -v dotnet)"
fi

# ローカル Worker (127.0.0.1:8787) への通信をプロキシに流さない
export NO_PROXY="127.0.0.1,localhost,::1"
export no_proxy="$NO_PROXY"

# WebKitGTK 6.0 のサンドボックス無効化 (Ubuntu 24.04 非特権名前空間制限のクラッシュ回避)
export WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS="${WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS:-1}"

echo "=== ChBrowser Linux ローカル起動 ==="
"$DOTNET" build "$REPO_ROOT/src/ChBrowser.Gtk/ChBrowser.Gtk.csproj" --artifacts-path "$ARTIFACTS" -v quiet -nologo
exec "$DOTNET" "$ARTIFACTS/bin/ChBrowser.Gtk/debug/ChBrowser.Gtk.dll" "$@"
