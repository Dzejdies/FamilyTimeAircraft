# Family Time Aircraft

A BepInEx 5 mod that adds buildable aircraft and airfields, child pilots and passengers, parachute drops, destination and crash orders, impact damage, and repairable wrecks. The aircraft is added to the fourth tile of the native building menu. The fictional Aster Twin Spires are an additional native build entry.

See [installation instructions](INSTALLATION.md), [gameplay and controls](docs/GAMEPLAY.md), and [release requirements](docs/RELEASE.md).

## Quick start

- Install a package matching your game build and renderer.
- R boards or exits an aircraft. F remains the native child go-to command. F8 opens flight controls near an aircraft.
- Solo play does not require Family Time Multiplayer. Multiplayer requires the matching Family Time Multiplayer 0.1.14 (protocol 20) and aircraft packages on every participant.

## Compatibility

| Component | Compatible version / target |
| --- | --- |
| Family Time | Patreon Build 37 or Build 91; use a build made for that game's assemblies |
| Family Time Multiplayer | Optional for solo; version 0.1.14, protocol 20, required for multiplayer aircraft |
| Family Time Aircraft | Version 0.1.3 on every multiplayer participant |
| Mod loader | BepInEx 5, compatible with the selected game build |
| Linux / Steam Deck | Build 91 Linux under Proton; BepInEx launch option: `WINEDLLOVERRIDES="winhttp=n,b" %command%` |

Build 37 and Build 91 use different native game assemblies, so their DLLs are not interchangeable. Build 91 Linux, Windows DX11, and Windows DX12 variants can share a multiplayer session when using matching mod versions. Build 37 peers must use the same game build. Current 0.1.3 package targets are Build 37 Windows DX11 and Build 91 Linux, Windows DX11, and Windows DX12. Build 91's stripped assemblies may require compatible full Unity reference assemblies when building from source.

Linux runtime checks and Windows checks through Proton have been performed. Native Windows runtime behavior has not been verified. No other gameplay mods have been compatibility-tested; plugins that alter construction menus, input, save/load, or physics may conflict.
