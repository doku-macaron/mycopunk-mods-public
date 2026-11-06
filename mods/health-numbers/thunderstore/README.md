# Health Numbers

Shows your health and your teammates' health as numbers (`35.0 / 35.0`). The game never shows the real values, and its health bar is not even proportional: at 50% health the bar is only about one third full. This mod adds the numbers next to the bars and changes nothing else.

## Features

- Your health as `current / max` to the right of your health bar, in the game's own HUD font.
- While your max health is temporarily capped (for example, the Mycologist consuming max health, or some objectives), the original max is added in brackets: `20.0 / 28.0 (35.0)`.
- Each teammate's health on its own line under their name in the teammate HUD (`HP 45.6 / 48.8`), in the same font and colour as their name. It lines up with DPS Checker's line instead of overlapping it.
- Hidden while you are dead and while the health bar is hidden (menus, HUD off), unless you change that.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.healthnumbers.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` \* | true | Show the health numbers |
| `Display.Format` | `{0} / {1}` | Text to show. `{0}` is current health, `{1}` is max health (the capped max while capped) |
| `Display.Decimals` | 1 | Digits after the decimal point (0–2, rounded) |
| `Display.CapFormat` | ` ({0})` | Added while max health is capped. `{0}` is the original max. Leave empty to hide |
| `Display.HideWhenDead` \* | true | Hide the text while the player is dead |
| `Position.Anchor` | HealthBar | `HealthBar`: right of the health bar. `Screen`: at `ScreenX` / `ScreenY` |
| `Position.HideWithHealthBar` \* | true | Hide the text while the game's health bar is hidden |
| `Position.OffsetX` / `OffsetY` | 12 / 0 | Offset from the bar, in pixels at 1080p |
| `Position.ScreenX` / `ScreenY` | 0.02 / 0.93 | Position for `Screen`, as a fraction of the screen width / height |
| `Style.Renderer` | Game | `Game`: the game's HUD font. `OnGUI`: a plain overlay on top of everything |
| `Style.FontSize` | 22 | Font size in pixels at 1080p |
| `Style.Bold` \* | true | Bold text |
| `Style.Color` \* | white | Text colour (colour picker in the MODS tab; the opacity stays as in the config) |
| `Style.Shadow` \* | true | Drop shadow behind the text |
| `Style.ShadowColor` | black, 80% | Shadow colour |
| `Teammates.Enabled` \* | true | Show teammates' health under their names |
| `Teammates.FontScale` | 0.9 | Size relative to the teammate's name |
| `Teammates.UseNameColor` \* | true | Use the teammate's name colour. Off: use `Style.Color` |
| `Teammates.OffsetX` / `OffsetY` | 6 / 0 | No longer used (kept so old config files stay valid) |
| `Debug.StatDumpKey` | None | (debug) Key that writes your base and current stats to `BepInEx/HealthNumbers/stats-*.md` |
| `Debug.DamageLog` \* | false | (debug) Log every hit you take (damage, element, source, health lost) to the BepInEx log |

## Multiplayer

Client-side only. The game already sends every player's health and max health to everyone, so you see your teammates' numbers with only this mod on your side. Neither the host nor your teammates need it.

## Notes

- Teammates' temporary max-health caps can't be shown: that value only exists on their own machine.
- While a teammate is dead, their line stays but is empty, so the lines below don't move.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
