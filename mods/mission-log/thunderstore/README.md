# Mission Log

Keeps a record of your missions. After every mission it adds one row to `BepInEx/MissionLog/missions-v1.tsv`: what you played, how it went, your loadout, the game's own mission statistics, and how smoothly the game ran. The file is a UTF-8, tab-separated table with a header row, so you can paste it straight into a spreadsheet.

## Features

- One row per mission, with the outcome: `success`, `fail`, or `left` (the mission ended without the game's normal end, for example a disconnect or quitting the game).
- Columns include:
  - start time and duration (real time),
  - mission, region, seed, threat and intensity,
  - number of players, your character, both weapons and your grenade,
  - the game's per-mission statistics (enemies killed and the other values the game tracks for the mission),
  - your max DPS and up to 3 teammates' name, character, max DPS and kills, when DPS Checker is installed,
  - performance: frame count, average FPS, 1% low FPS, the slowest frame, and how many frames took over 25 / 50 / 100 ms.
- Mission and region names are written in the game's current language.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)
- Optional: DPS Checker, to fill in the DPS and teammate columns

## Settings

Mission Log has no settings.

## Multiplayer

Client-side only. It only reads the game's data and writes its own file, so other players don't need it. It also works when you join someone else's mission.

## Notes

- It never touches your save; the only file it writes is its own log.
- Leaving a mission from the menu counts as a failure in the game, so that row says `fail`, not `left`.
- When you join a mission that is already running, the time and frame columns count from the moment you joined.
- For a `left` row, the resource and upgrade columns are empty.
- Teammate DPS values marked as estimated come from DPS Checker's estimate for players who don't have it.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
