#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

export DOCKER_CONFIG=/tmp/.docker

echo "============================================================"
echo "ChBrowser Linux (GNOME/GTK4/libadwaita) Comprehensive Verification"
echo "============================================================"

# ホスト環境でデスクトップファイル・メタデータの検証
echo "=== Step 1: Desktop Entry & Metainfo Validation ==="
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "${REPO_ROOT}/data/desktop/org.chbrowser.chbrowser.desktop"
    echo "✔ desktop-file-validate passed"
fi

if command -v appstreamcli >/dev/null 2>&1; then
    appstreamcli validate --no-net "${REPO_ROOT}/data/desktop/org.chbrowser.chbrowser.metainfo.xml"
    echo "✔ appstreamcli validate passed"
fi

# インストーラースクリプトの検証
echo "=== Step 2: Desktop Integration Installation / Uninstallation Test ==="
"${REPO_ROOT}/tools/install-desktop.sh" --prefix /tmp/chbrowser_verify_prefix
test -f /tmp/chbrowser_verify_prefix/share/applications/org.chbrowser.chbrowser.desktop
test -f /tmp/chbrowser_verify_prefix/share/metainfo/org.chbrowser.chbrowser.metainfo.xml
test -f /tmp/chbrowser_verify_prefix/share/icons/hicolor/scalable/apps/org.chbrowser.chbrowser.svg
test -f /tmp/chbrowser_verify_prefix/bin/chbrowser
"${REPO_ROOT}/tools/install-desktop.sh" --prefix /tmp/chbrowser_verify_prefix --uninstall
rm -rf /tmp/chbrowser_verify_prefix
echo "✔ Installer & Uninstaller passed"

# Docker コンテナ内で単体テストおよび GTK4 / WebKitGTK 6.0 ヘッドレス実行検証
echo "=== Step 3: Container Tests (Core Unit Tests + GTK4 UI Verification) ==="
docker run --rm \
  -v "$REPO_ROOT:/work" \
  -w /work \
  -e WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1 \
  chbrowser-gtk:latest \
  bash -c '
    set -euo pipefail
    echo "--- 3a. Running ChBrowser.Core Unit Tests (including XDG Base Directory) ---"
    dotnet test tests/ChBrowser.Core.Tests/ChBrowser.Core.Tests.csproj

    echo "--- 3b. Running GTK4 / libadwaita Headless Verification ---"
    Xvfb :99 -screen 0 1280x800x24 &
    XVFB_PID=$!
    sleep 1
    export DISPLAY=:99
    
    dotnet run --project src/ChBrowser.Gtk/ChBrowser.Gtk.csproj -- --test
    EXIT_CODE=$?
    kill $XVFB_PID 2>/dev/null || true
    chmod -R ugo+rwX /work/src /work/tests /work/data 2>/dev/null || true
    
    if [ $EXIT_CODE -eq 0 ]; then
        echo "✔ All tests and GTK4 headless verifications PASSED!"
    else
        echo "✘ Verification failed with code $EXIT_CODE"
        exit $EXIT_CODE
    fi
  '

echo "============================================================"
echo "✔ All Phase-5 verifications completed successfully!"
echo "============================================================"
