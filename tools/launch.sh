#!/usr/bin/env bash
set -euo pipefail
GAME_DIR="${FAMILYTIME_DIR:-$HOME/Games/Family Time}"
PROTON_DIR="${PROTON_DIR:-$HOME/.local/share/Steam/compatibilitytools.d/GE-Proton11-5-x86_64}"
export STEAM_COMPAT_DATA_PATH="${STEAM_COMPAT_DATA_PATH:-$HOME/.local/share/Steam/steamapps/compatdata/3364698173}"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="${STEAM_COMPAT_CLIENT_INSTALL_PATH:-$HOME/.local/share/Steam}"
export WINEDLLOVERRIDES="winhttp=n,b"
cd "$GAME_DIR"
exec "$PROTON_DIR/proton" run ./FamilyTime.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 "$@"
