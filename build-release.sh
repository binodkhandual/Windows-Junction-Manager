#!/usr/bin/env bash
set -e

echo "========================================================"
echo "      Windows Junction Manager - Release Builder"
echo "========================================================"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CSPROJ="JunctionManagerWpf/JunctionManager.csproj"
ABOUT_XAML="JunctionManagerWpf/AboutWindow.xaml"

CURRENT_VER=$(grep -oPm1 "(?<=<Version>)[^<]+" "$CSPROJ" || echo "2.1.0")

VERSION="$1"
if [ -z "$VERSION" ]; then
    echo "Current version in csproj: $CURRENT_VER"
    read -rp "Enter release version [press Enter for $CURRENT_VER]: " INPUT_VER
    VERSION="${INPUT_VER:-$CURRENT_VER}"
fi

# Strip leading 'v' or 'V'
VERSION="${VERSION#v}"
VERSION="${VERSION#V}"

echo ""
echo "[*] Building Release for Version: v${VERSION}"
echo ""

# Update csproj and AboutWindow if changed
if [ "$VERSION" != "$CURRENT_VER" ]; then
    echo "[*] Updating version in $CSPROJ and $ABOUT_XAML..."
    sed -i "s|<Version>.*</Version>|<Version>$VERSION</Version>|g" "$CSPROJ"
    sed -i "s|Text=\"v.*\"|Text=\"v$VERSION\"|g" "$ABOUT_XAML"
fi

mkdir -p dist

echo "[*] Publishing single-file executable..."
dotnet publish "$CSPROJ" -c Release -r win-x64 -p:PublishSingleFile=true -p:Version="$VERSION" --self-contained false -o dist

VERSIONED_EXE="dist/JunctionManager-v${VERSION}-win-x64.exe"
cp "dist/JunctionManager.exe" "$VERSIONED_EXE"

echo ""
echo "========================================================"
echo "[SUCCESS] Release build completed!"
echo "- Standard:  dist/JunctionManager.exe"
echo "- Versioned: $VERSIONED_EXE"
echo "========================================================"
echo ""

read -rp "Do you want to create and push git tag 'v${VERSION}' to trigger GitHub Release? [y/N]: " DO_GIT
if [[ "$DO_GIT" =~ ^[Yy]$ ]]; then
    git add "$CSPROJ" "$ABOUT_XAML"
    git commit -m "Bump version to v${VERSION}" || true
    git tag -a "v${VERSION}" -m "Release v${VERSION}" -f
    git push origin "v${VERSION}"
    echo ""
    echo "[OK] Tag v${VERSION} pushed successfully! GitHub Actions will publish the release."
fi
