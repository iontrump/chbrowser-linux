#!/usr/bin/env bash
set -euo pipefail

# ChBrowser Debian (.deb) Package Builder
# Builds a standalone .deb package containing the self-contained single-file executable,
# desktop integration files (desktop entry, AppStream metainfo, SVG icon), and post-install triggers.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

VERSION="${1:-1.0}"
APP_ID="org.chbrowser.chbrowser"

# Detect architecture
RAW_ARCH="$(uname -m)"
case "$RAW_ARCH" in
    x86_64|amd64)
        DEB_ARCH="amd64"
        DOTNET_RID="linux-x64"
        ;;
    aarch64|arm64)
        DEB_ARCH="arm64"
        DOTNET_RID="linux-arm64"
        ;;
    *)
        echo "Error: Unsupported architecture: $RAW_ARCH"
        exit 1
        ;;
esac

echo "=== Building ChBrowser .deb package ==="
echo "Version      : ${VERSION}"
echo "Architecture : ${DEB_ARCH} (RID: ${DOTNET_RID})"

PUBLISH_DIR="${REPO_ROOT}/src/ChBrowser.Gtk/bin/Release/net8.0/${DOTNET_RID}/publish"
BINARY_PATH="${PUBLISH_DIR}/ChBrowser.Gtk"

# Ensure single-file binary is built
if [[ ! -f "${BINARY_PATH}" ]]; then
    echo "Single-file binary not found at ${BINARY_PATH}."
    echo "Building single binary via container/dotnet publish..."
    if command -v docker >/dev/null 2>&1 && docker image inspect chbrowser-gtk:latest >/dev/null 2>&1; then
        export DOCKER_CONFIG=/tmp/.docker
        docker run --rm \
          -v "$REPO_ROOT:/work" \
          -w /work \
          chbrowser-gtk:latest \
          dotnet publish src/ChBrowser.Gtk/ChBrowser.Gtk.csproj \
            -c Release \
            -r "$DOTNET_RID" \
            --self-contained true \
            -p:PublishSingleFile=true
    else
        dotnet publish "${REPO_ROOT}/src/ChBrowser.Gtk/ChBrowser.Gtk.csproj" \
          -c Release \
          -r "$DOTNET_RID" \
          --self-contained true \
          -p:PublishSingleFile=true
    fi
fi

if [[ ! -f "${BINARY_PATH}" ]]; then
    echo "Error: Failed to produce binary at ${BINARY_PATH}"
    exit 1
fi

DIST_DIR="${REPO_ROOT}/dist"
mkdir -p "${DIST_DIR}"

PACKAGE_NAME="chbrowser_${VERSION}_${DEB_ARCH}"
STAGING_DIR="$(mktemp -d -t chbrowser_deb_XXXXXX)"
chmod 755 "${STAGING_DIR}"
trap 'rm -rf "${STAGING_DIR}"' EXIT

echo "Staging directory: ${STAGING_DIR}"

# Directory layout
mkdir -p "${STAGING_DIR}/DEBIAN"
chmod 755 "${STAGING_DIR}/DEBIAN"
mkdir -p "${STAGING_DIR}/usr/lib/chbrowser"
mkdir -p "${STAGING_DIR}/usr/bin"
mkdir -p "${STAGING_DIR}/usr/share/applications"
mkdir -p "${STAGING_DIR}/usr/share/metainfo"
mkdir -p "${STAGING_DIR}/usr/share/icons/hicolor/scalable/apps"

# 1. DEBIAN/control
INSTALLED_SIZE=$(du -sk "${BINARY_PATH}" | cut -f1)
cat << EOF > "${STAGING_DIR}/DEBIAN/control"
Package: chbrowser
Version: ${VERSION}
Section: net
Priority: optional
Architecture: ${DEB_ARCH}
Installed-Size: ${INSTALLED_SIZE}
Maintainer: ChBrowser Developers <https://github.com/pawapawa041950/chbrowser>
Depends: libc6, libgtk-4-1, libadwaita-1-0, libwebkitgtk-6.0-4
Recommends: fcitx5-frontend-gtk4 | ibus-gtk4
Homepage: https://github.com/iontrump/chbrowser-linux
Description: Modern 4-pane BBS browser for Linux (GNOME/GTK4)
 ChBrowser is a fast, responsive 4-pane BBS browser for 5ch, BBSPINK,
 open2ch, Eddi, and Machi BBS, built with C# (.NET 8) and GTK4/WebKitGTK.
 Features fully local operation, inline media viewing, tree replies,
 and self-contained HTML thread exporting.
EOF

# 2. DEBIAN/postinst & postrm
cat << 'EOF' > "${STAGING_DIR}/DEBIAN/postinst"
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
EOF
chmod 755 "${STAGING_DIR}/DEBIAN/postinst"

cat << 'EOF' > "${STAGING_DIR}/DEBIAN/postrm"
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
    fi
fi
exit 0
EOF
chmod 755 "${STAGING_DIR}/DEBIAN/postrm"

# 3. /usr/lib/chbrowser/chbrowser (Executable binary)
cp -f "${BINARY_PATH}" "${STAGING_DIR}/usr/lib/chbrowser/chbrowser"
chmod 755 "${STAGING_DIR}/usr/lib/chbrowser/chbrowser"

# 4. /usr/bin/chbrowser (Launcher wrapper)
cat << 'EOF' > "${STAGING_DIR}/usr/bin/chbrowser"
#!/usr/bin/env bash
set -e

# Ubuntu 24.04 unprivileged user namespace workaround for WebKitGTK 6.0
export WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS="${WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS:-1}"

# Ensure correct fcitx5 GTK4 input module
if [ "${GTK_IM_MODULE:-}" = "fcitx" ]; then
    export GTK_IM_MODULE=fcitx5
elif [ -z "${GTK_IM_MODULE:-}" ] && [[ "${XMODIFIERS:-}" == *"@im=fcitx"* ]]; then
    export GTK_IM_MODULE=fcitx5
fi

exec /usr/lib/chbrowser/chbrowser "$@"
EOF
chmod 755 "${STAGING_DIR}/usr/bin/chbrowser"

# 5. Desktop file, Metainfo, and Icon
cp -f "${REPO_ROOT}/data/desktop/${APP_ID}.desktop" "${STAGING_DIR}/usr/share/applications/${APP_ID}.desktop"
chmod 644 "${STAGING_DIR}/usr/share/applications/${APP_ID}.desktop"

cp -f "${REPO_ROOT}/data/desktop/${APP_ID}.metainfo.xml" "${STAGING_DIR}/usr/share/metainfo/${APP_ID}.metainfo.xml"
chmod 644 "${STAGING_DIR}/usr/share/metainfo/${APP_ID}.metainfo.xml"

cp -f "${REPO_ROOT}/data/icons/hicolor/scalable/apps/${APP_ID}.svg" "${STAGING_DIR}/usr/share/icons/hicolor/scalable/apps/${APP_ID}.svg"
chmod 644 "${STAGING_DIR}/usr/share/icons/hicolor/scalable/apps/${APP_ID}.svg"

# 6. Build .deb package
OUTPUT_DEB="${DIST_DIR}/${PACKAGE_NAME}.deb"
echo "Building .deb package: ${OUTPUT_DEB}..."
dpkg-deb --build --root-owner-group "${STAGING_DIR}" "${OUTPUT_DEB}"

echo ""
echo "=== Package Build Successful ==="
echo "Package File: ${OUTPUT_DEB}"
echo "Package Size: $(du -h "${OUTPUT_DEB}" | cut -f1)"
echo ""
echo "Package Info:"
dpkg-deb -I "${OUTPUT_DEB}"
echo ""
echo "Package Contents:"
dpkg-deb -c "${OUTPUT_DEB}"
