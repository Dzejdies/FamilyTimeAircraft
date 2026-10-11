#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
output_dir="${1:?Provide an absolute output directory}"
shift
[[ "$output_dir" = /* ]] || { printf '%s\n' 'Build output must be an absolute directory.' >&2; exit 1; }
for argument in "$@"; do
    case "${argument,,}" in
        -p:*gamedir=*|/p:*gamedir=*|-property:*gamedir=*|/property:*gamedir=*)
            printf '%s\n' 'Set FAMILYTIME_DIR instead of overriding GameDir; install and build must use the same destination.' >&2
            exit 1 ;;
    esac
done
game_dir="${FAMILYTIME_DIR:-$HOME/Games/Family Time}"
artifacts_dir="$(mktemp -d)"
trap 'rm -rf "$artifacts_dir"' EXIT
multiplayer_project_dir="${MULTIPLAYER_PROJECT_DIR:-$PWD/../FamilyTimeMultiplayer-aircraft}"
project="src/FamilyTimeAircraft/FamilyTimeAircraft.csproj"
if [[ -f "$multiplayer_project_dir/src/FamilyTimeMultiplayer/FamilyTimeMultiplayer.csproj" ]]; then
    project="src/FamilyTimeAircraft.Multiplayer/FamilyTimeAircraft.Multiplayer.csproj"
fi
dotnet build "$project" -c Release --artifacts-path "$artifacts_dir" -p:GameDir="$game_dir" -p:MultiplayerProjectDir="$multiplayer_project_dir" -p:OutputPath="$output_dir/" -p:PathMap="$artifacts_dir=/_build" -p:IncludeSourceRevisionInInformationalVersion=false "$@"
