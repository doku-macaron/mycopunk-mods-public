# Mycopunk Mod Kit

A small library that the other doku_macaron mods for Mycopunk depend on. It changes nothing in the game by itself: it provides shared building blocks so that every mod uses the same version of them. Mod managers install it automatically when you install one of those mods.

## Features

- **MODS tab in the Settings window.** Adds a "MODS" tab below Graphics, where the options of every installed mod that supports it are listed:
  - a heading per mod,
  - ON/OFF rows and colour pickers that look like the game's own rows,
  - English or Japanese names (following the game's language), with a description when you hover a row.
  - Values are saved to each mod's BepInEx config file. The game's own settings file and your save are never touched.
  - The tab is there both on the title screen and in the pause menu during a mission.
- **Hotkey conflict report.** Once per launch it writes to the BepInEx log every key that is used by more than one mod, or by a mod and the game's key bindings (including your rebinds).
- **Shared pieces used by other mods**, such as extra lines under teammates' names in the teammate HUD (so Health Numbers and DPS Checker stack neatly instead of overlapping), and passing values between mods (for example, DPS Checker values into Mission Log).

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)

## Settings

Mod Kit has no settings of its own. The MODS tab only shows the options of the other mods you have installed.

## Multiplayer

Client-side only. It only adds a tab to your local Settings window and writes to your log, so other players don't need it. As with any BepInEx mod, your game counts as modded.

## Notes

- The MODS tab is built from copies of the game's own menu rows. If a game update changes the Settings window so that the tab can't be built, Mod Kit logs a warning and the rest of the game keeps working.
- Keys that the game reads directly (not through its key bindings) can't be seen by the conflict report.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
