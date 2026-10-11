#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
variant="${1:?Choose build37-windows-dx11, build91-linux, build91-windows-dx11, or build91-windows-dx12}"
shift
case "$variant" in
    build37-windows-dx11|build91-linux|build91-windows-dx11|build91-windows-dx12) ;;
    *) printf '%s\n' "Unknown game variant: $variant" >&2; exit 1 ;;
esac
package_dir="$(mktemp -d)"
trap 'rm -rf "$package_dir"' EXIT
tools/build.sh "$package_dir/build" "$@"
aircraft_version="$(sed -n 's/.*public const string Version = "\([0-9.]*\)";.*/\1/p' src/FamilyTimeAircraft/Plugin.cs)"
[[ -n "$aircraft_version" ]] || { printf '%s\n' 'Missing aircraft release version.' >&2; exit 1; }
plugin_dir="$package_dir/BepInEx/plugins/FamilyTimeAircraft"
mkdir -p "$plugin_dir/assets" dist
rm -f dist/SHA256SUMS dist/release-manifest.json
cp "$package_dir/build/FamilyTimeAircraft.dll" "$package_dir/build/FamilyTimeAircraft.Core.dll" "$plugin_dir/"
cp assets/*.fta assets/*-thumbnail.png "$plugin_dir/assets/"
cp README.md "$package_dir/"
printf '%s\n' "Game variant: $variant" "Aircraft version: $aircraft_version" 'Optional multiplayer requirement: Family Time Multiplayer 0.1.14, protocol 20' > "$package_dir/BUILD.txt"
mkdir -p "$package_dir/docs"
cp docs/GAMEPLAY.md docs/RELEASE.md "$package_dir/docs/"
if [[ -f "$package_dir/build/FamilyTimeAircraft.Multiplayer.dll" ]]; then
    mkdir -p "$plugin_dir/integrations"
    cp "$package_dir/build/FamilyTimeAircraft.Multiplayer.dll" "$plugin_dir/integrations/"
    companion_dir="$(mktemp -d)"
    trap 'rm -rf "$package_dir" "${companion_dir:-}"' EXIT
    mkdir -p "$companion_dir/BepInEx/plugins"
    cp "$package_dir/build/FamilyTimeMultiplayer.dll" "$package_dir/build/FamilyTimeMultiplayer.Core.dll" "$companion_dir/BepInEx/plugins/"
    printf '%s\n' "Game variant: $variant" 'Family Time Multiplayer: 0.1.14, protocol 20' 'Requires an existing working BepInEx 5 installation. Extract BepInEx into the matching game installation.' 'Install the matching Family Time Aircraft archive separately. Every multiplayer participant must use the same mod versions.' 'Keep BepInEx/config/ftmp-player.key when upgrading: it identifies your saved family.' > "$companion_dir/README.txt"
    python3 - "$companion_dir" "$PWD/dist/FamilyTimeMultiplayer-0.1.14-p20-$variant.zip" <<'PYZIP'
import pathlib, sys, zipfile
root = pathlib.Path(sys.argv[1])
with zipfile.ZipFile(sys.argv[2], 'w', zipfile.ZIP_DEFLATED) as archive:
    for file in sorted(root.rglob('*')):
        if file.is_file(): archive.write(file, file.relative_to(root))
PYZIP
fi
rm -rf "$package_dir/build"
python3 - "$package_dir" "$PWD/dist/FamilyTimeAircraft-$aircraft_version-$variant.zip" <<'PY'
import pathlib, sys, zipfile
root = pathlib.Path(sys.argv[1])
with zipfile.ZipFile(sys.argv[2], 'w', zipfile.ZIP_DEFLATED) as archive:
    for file in sorted(root.rglob('*')):
        if file.is_file(): archive.write(file, file.relative_to(root))
PY
