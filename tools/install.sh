#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
game_dir="${FAMILYTIME_DIR:-$HOME/Games/Family Time}"
build_dir="$(mktemp -d)"
trap 'rm -rf "$build_dir"' EXIT
tools/build.sh "$build_dir" "$@"
plugin_dir="$game_dir/BepInEx/plugins/FamilyTimeAircraft"
mkdir -p "$plugin_dir/assets"
cp "$build_dir/FamilyTimeAircraft.dll" "$build_dir/FamilyTimeAircraft.Core.dll" "$plugin_dir/"
cp assets/*.fta assets/*-thumbnail.png "$plugin_dir/assets/"
legacy_adapter="$plugin_dir/FamilyTimeAircraft.Multiplayer.dll"
if [[ -f "$legacy_adapter" ]]; then
    mv "$legacy_adapter" "$legacy_adapter.disabled"
fi
if [[ -f "$build_dir/FamilyTimeAircraft.Multiplayer.dll" ]]; then
    mkdir -p "$plugin_dir/integrations"
    cp "$build_dir/FamilyTimeAircraft.Multiplayer.dll" "$plugin_dir/integrations/"
else
    rm -f "$plugin_dir/integrations/FamilyTimeAircraft.Multiplayer.dll"
fi
