# Installation and development

## Install a release

1. Choose the archive matching your game build and renderer: `build37-windows-dx11`, `build91-linux`, `build91-windows-dx11`, or `build91-windows-dx12`. Do not mix packages built for different game assemblies.
2. Install a working BepInEx 5 loader for that game build. The aircraft archive does not include BepInEx or proprietary game assemblies.
3. Extract the archive into the game folder containing `FamilyTime.exe`, merging its `BepInEx` directory. Keep `FamilyTimeAircraft.dll`, `FamilyTimeAircraft.Core.dll`, and the `assets` folder together.
4. Start the game. The aircraft construction option is added to the native building menu. R boards/exits; F remains the native child go-to command; F8 opens flight controls near an aircraft.

Solo play needs only the aircraft mod. For multiplayer, install the matching Family Time Multiplayer 0.1.14 (protocol 20) package on every participant, plus the aircraft package for the same game variant. The aircraft multiplayer adapter is installed under `BepInEx/plugins/FamilyTimeAircraft/integrations`. Preserve `BepInEx/config/ftmp-player.key` across updates.

On Linux/Steam Deck with Proton, use the launch option required by BepInEx: `WINEDLLOVERRIDES="winhttp=n,b" %command%`.

## Build from source

Requirements: .NET 8 SDK, the managed Unity/game assemblies for the target installation, and a working BepInEx setup for installation. Game assemblies are proprietary and are not included.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
FAMILYTIME_DIR="/path/to/Family Time" tools/install.sh
```

To make a redistributable archive without installing it, run `FAMILYTIME_DIR="/path/to/Family Time" tools/package.sh build91-linux` (replace the variant with the target listed above). The optional multiplayer integration is built when the Multiplayer SDK source is available in the configured sibling checkout. Set `MULTIPLAYER_PROJECT_DIR` to that checkout if necessary. Packaging writes to `dist/`. Build 91 stripped assemblies may require compatible full Unity reference assemblies.
