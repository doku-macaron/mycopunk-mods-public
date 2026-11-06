# doku_macaron's Mycopunk mods

BepInEx 5 mods for [Mycopunk](https://store.steampowered.com/app/3247750/). Install them from Thunderstore (r2modman / Gale); each mod pulls in Mycopunk Mod Kit and BepInExPack_Mycopunk automatically.

Not affiliated with Pigeons at Play or Devolver Digital.

| Mod | What it does | Install |
|---|---|---|
| [ModKit](mods/modkit) | Shared library for doku_macaron mods: adds a MODS tab to the Options window (ON/OFF rows, colour pickers, English/Japanese labels) and logs hotkey conflicts. Does nothing on its own. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/ModKit/) |
| [UpgradeLagFix](mods/upgrade-lag-fix) | Removes the freezes when many upgrades are stacked on one gear (Ouroboros/Blitz). Client-side, no gameplay changes: same stats, same apply order, only faster lookups. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/UpgradeLagFix/) |
| [QuickTimers](mods/quick-timers) | Shorter fixed waits: skips the startup animations, and (opt-in) shortens the mission drop, start, end and extraction delays. The host's settings apply to the lobby. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/QuickTimers/) |
| [HealthNumbers](mods/health-numbers) | Shows your health as numbers next to the health bar, and each teammate's health under their name in the teammate HUD. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/HealthNumbers/) |
| [DPSChecker](mods/dps-checker) | Shows your and your teammates' DPS, best DPS and kills on the HUD. Players who have it share exact values; others are estimated. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/DPSChecker/) |
| [MissionLog](mods/mission-log) | Writes one line per mission (result, time, mission, threat, modifiers, DPS...) to a TSV file, so you can look back at your runs. Read-only: never touches the save. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/MissionLog/) |
| [MinimapNorthUp](mods/minimap-north-up) | Keeps the minimap north-up, like the full-screen map: your arrow turns instead of the map. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/MinimapNorthUp/) |
| [PingDistance](mods/ping-distance) | Shows the distance in metres under pings and objective markers. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/PingDistance/) |
| [AbilityCooldown](mods/ability-cooldown) | Shows the seconds until the next charge on ability icons, with charge counts for stacking abilities. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/AbilityCooldown/) |
| [ThrowPreview](mods/throw-preview) | Shows the arc and landing point of grenades (hold to aim, release to throw) and of held objects such as a teammate's parts, coloured when the throw would revive them. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/ThrowPreview/) |
| [XPNumbers](mods/xp-numbers) | Adds the XP into the current level and the XP the next level needs to the level bars (class screen, build screen and mission results). | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/XPNumbers/) |
| [WeaponCycle](mods/weapon-cycle) | Puts a picked-up heavy weapon into the scroll wheel and weapon 1 key cycle, and lets the wheel skip weapons that are out of ammo. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/WeaponCycle/) |
| [SystemMonitor](mods/system-monitor) | Shows FPS and your ping to the host in a corner of the screen. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/SystemMonitor/) |
| [SpeedMonitor](mods/speed-monitor) | Shows your current movement speed under the health bar. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/SpeedMonitor/) |
| [HealthRegenTimer](mods/health-regen-timer) | Shows the seconds until health regeneration starts, above the health bar. | [Thunderstore](https://thunderstore.io/c/mycopunk/p/doku_macaron/HealthRegenTimer/) |

## Building

Needs the .NET 8 SDK and the game with BepInExPack_Mycopunk installed (the build references the game's DLLs).

```
dotnet build mycopunk-mods.sln -c Release
```

- The game folder defaults to `D:\SteamLibrary\steamapps\common\Mycopunk`. Put a `GamePath.props` with `<Project><PropertyGroup><GamePath>…</GamePath></PropertyGroup></Project>` at the root to change it (git-ignored).
- `-p:DeployToGame=true` also copies each built mod into the game's `BepInEx/plugins`.
- `pwsh packaging/package.ps1` builds the Thunderstore zips into `dist/`.

Shared code (MODS tab, hotkey report, teammate HUD lines) lives in `shared/` and ships as its own plugin, Mycopunk Mod Kit (`mods/modkit`); every other mod depends on it.

## License

MIT, see [LICENSE](LICENSE).
