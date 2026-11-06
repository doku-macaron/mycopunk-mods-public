# DPS Checker

Shows how much damage each player is dealing to enemies (and to the practice targets in the lobby) as DPS over the last second. Your own numbers appear above your weapon box, and each teammate's appear under their name in the teammate HUD, in the same font and colour as the game's HUD.

## Features

- A line like `DPS 1.2k  T 34.5k  K 12`:
  - `DPS`: damage dealt over the last 1 second,
  - `T`: total damage since the count started,
  - `K`: enemy cores destroyed.
- Values under 1,000 are shown as they are (`654`); larger ones are shortened (`1.2k`).
- The count resets when a mission starts and when you leave it.
- In multiplayer, players who have the mod share their exact values through the host.
- For teammates without shared values, an estimate from the damage and kills your game can see is shown with a `~` (`DPS ~1.2k  T ~3.4k  K ~12`).
- If your weapon HUD can't be found, your own line is shown at the top left of the screen instead.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)

## Settings

DPS Checker has no settings.

## Multiplayer

- Your own DPS works with only this mod on your side.
- To see a teammate's **exact** values, both the host and that teammate need the mod: each player's values are sent to the host and shared from there.
- Without that, your game shows an estimate (`~`) for the teammate, also when the host doesn't have the mod.
- Older versions of the mod still share DPS, but the total damage is only shared correctly when the host and the measured player use the latest version.
- The teammate line sits in the same row system as Health Numbers, so the two don't overlap.

## Notes

- Damage values are the ones the game reports, so things like overkill depend on how the game counts them.
- Estimates leave out attacks whose attacker or damage your game can't identify, so they can be lower than, or different from, the real values. `~0` means nothing could be measured.
- If estimates never appear, look for `DPS estimate:` lines in `BepInEx/LogOutput.log`. After a game update or with other mods, estimation turns itself off for enemies it can't read safely.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: f1w3
Not affiliated with Pigeons at Play or Devolver Digital.
