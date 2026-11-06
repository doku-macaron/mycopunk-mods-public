# Health Regen Timer

Shows how long it will be until your health starts regenerating, as a small timer above your health bar (for example `6.5 s`). It uses the game's own regeneration delay, including temporary effects that change it, so you know exactly when it is safe to wait for health to come back.

## Features

- Seconds until automatic health regeneration begins, above your health bar, in the game's HUD font.
- Shown only while you are waiting for regeneration to start.
- Hidden when your health is full, while it is already regenerating, while you are dead, while the health bar is hidden, and on the mission results screen.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/f1w3.mycopunk.healthregentimer.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Mod Kit) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` \* | true | Show the time until health regeneration begins above your health bar |

## Multiplayer

Client-side only. It only adds text to your own HUD, so other players don't need it.

## Notes

- If a game update changes the code the timer reads, the timer turns itself off and logs a warning; the game keeps working.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: f1w3
Not affiliated with Pigeons at Play or Devolver Digital.
