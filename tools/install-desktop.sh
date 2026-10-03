#!/usr/bin/env bash
set -euo pipefail

# ChBrowser Desktop Entry & Icon Installation Script
# Supports user-local (~/.local) and system (/usr/local) installation and uninstallation.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PREFIX="${HOME}/.local"
UNINSTALL=0

usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  --user             Install to ~/.local (default)"
    echo "  --system           Install to /usr/local (requires root privileges)"
    echo "  --prefix PATH      Specify custom install prefix"
    echo "  --uninstall        Uninstall ChBrowser desktop integration"
    echo "  -h, --help         Show this help message"
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --user)
            PREFIX="${HOME}/.local"
            shift
            ;;
        --system)
            PREFIX="/usr/local"
            shift
            ;;
        --prefix)
            PREFIX="$2"
            shift 2
            ;;
        --uninstall)
            UNINSTALL=1
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            echo "Unknown option: $1"
            usage
            ;;
    esac
done

APP_ID="org.chbrowser.chbrowser"
BIN_DIR="${PREFIX}/bin"
APP_DIR="${PREFIX}/share/applications"
META_DIR="${PREFIX}/share/metainfo"
ICON_DIR="${PREFIX}/share/icons/hicolor/scalable/apps"

if [[ "$UNINSTALL" -eq 1 ]]; then
    echo "=== Uninstalling ChBrowser desktop integration from ${PREFIX} ==="
    rm -f "${APP_DIR}/${APP_ID}.desktop"
    rm -f "${META_DIR}/${APP_ID}.metainfo.xml"
    rm -f "${ICON_DIR}/${APP_ID}.svg"
    rm -f "${BIN_DIR}/chbrowser"
    
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "${APP_DIR}" 2>/dev/null || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t "${PREFIX}/share/icons/hicolor" 2>/dev/null || true
    fi
    echo "Uninstallation complete."
    exit 0
fi

echo "=== Installing ChBrowser desktop integration to ${PREFIX} ==="

mkdir -p "${BIN_DIR}"
mkdir -p "${APP_DIR}"
mkdir -p "${META_DIR}"
mkdir -p "${ICON_DIR}"

# 1. Install Desktop File
cp -f "${REPO_ROOT}/data/desktop/${APP_ID}.desktop" "${APP_DIR}/${APP_ID}.desktop"
chmod 644 "${APP_DIR}/${APP_ID}.desktop"

# 2. Install AppStream Metainfo
cp -f "${REPO_ROOT}/data/desktop/${APP_ID}.metainfo.xml" "${META_DIR}/${APP_ID}.metainfo.xml"
chmod 644 "${META_DIR}/${APP_ID}.metainfo.xml"

# 3. Install Scalable SVG Icon
cp -f "${REPO_ROOT}/data/icons/hicolor/scalable/apps/${APP_ID}.svg" "${ICON_DIR}/${APP_ID}.svg"
chmod 644 "${ICON_DIR}/${APP_ID}.svg"

# 4. Install Runner Wrapper
cat << 'EOF' > "${BIN_DIR}/chbrowser"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EOF

echo "REPO_ROOT=\"${REPO_ROOT}\"" >> "${BIN_DIR}/chbrowser"

cat << 'EOF' >> "${BIN_DIR}/chbrowser"
export WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS="${WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS:-1}"

# GTK4 では fcitx5 のモジュール名は fcitx5
if [ "${GTK_IM_MODULE:-}" = "fcitx" ]; then
    export GTK_IM_MODULE=fcitx5
elif [ -z "${GTK_IM_MODULE:-}" ] && [[ "${XMODIFIERS:-}" == *"@im=fcitx"* ]]; then
    export GTK_IM_MODULE=fcitx5
fi

ARTIFACTS="${REPO_ROOT}/.build-local"
DLL_PATH="${ARTIFACTS}/bin/ChBrowser.Gtk/debug/ChBrowser.Gtk.dll"

if [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET="$HOME/.dotnet/dotnet"
    export DOTNET_ROOT="$HOME/.dotnet"
else
    DOTNET="$(command -v dotnet)"
fi

if [[ -f "${DLL_PATH}" ]]; then
    exec "$DOTNET" "${DLL_PATH}" "$@"
else
    "$DOTNET" build "${REPO_ROOT}/src/ChBrowser.Gtk/ChBrowser.Gtk.csproj" --artifacts-path "$ARTIFACTS" -v quiet -nologo
    exec "$DOTNET" "${DLL_PATH}" "$@"
fi
EOF

chmod +x "${BIN_DIR}/chbrowser"

# 5. Update Caches
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${APP_DIR}" 2>/dev/null || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t "${PREFIX}/share/icons/hicolor" 2>/dev/null || true
fi

echo "=== Installation complete ==="
echo "Desktop Entry : ${APP_DIR}/${APP_ID}.desktop"
echo "Metainfo      : ${META_DIR}/${APP_ID}.metainfo.xml"
echo "Icon          : ${ICON_DIR}/${APP_ID}.svg"
echo "Binary        : ${BIN_DIR}/chbrowser"
echo ""
echo "You can launch ChBrowser from your application launcher or by running:"
echo "  ${BIN_DIR}/chbrowser"
