# XP Numbers

Adds the actual XP numbers to the level bars: how much XP you have in the current level and how much the next level costs, for example `1,200 / 3,000 XP`.

## Features

- **Class screen** (right after pressing TAB): the numbers next to the level, slightly smaller.
- **Build screen** (weapons, gear and classes): the numbers to the right of the level under the bar.
- **Mission results** (the XP page): the numbers under the right end of each bar. They count up together with the bar while it fills.
- Hidden at max level (where the game shows "∞").

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.xpnumbers.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Mod Kit).

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` \* | true | Show the XP numbers next to the level bars. Takes effect the next time a bar is drawn |
| `Results.FontScale` | 0.7 | Size of the numbers on the mission results, relative to the level text (0.3–2) |

## Multiplayer

Client-side only. It only adds text to your own menus, so other players don't need it.

## Notes

- It only reads the XP values the game already has; it never changes your save.
- Turning it on or off shows up the next time a bar is drawn (for example, when you reopen the screen).

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
