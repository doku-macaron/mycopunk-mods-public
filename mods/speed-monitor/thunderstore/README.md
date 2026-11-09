# Speed Monitor

Shows your current movement speed under your health bar, for example `10.5 m/s`, in the game's HUD font. Handy for testing movement upgrades and abilities.

## Features

- Your speed in metres per second, updated about every 0.05 seconds.
- Position speed uses the distance moved over about 0.5 seconds and is smoothed to reduce fluctuations; stopping is shown immediately, while acceleration and deceleration appear slightly later.
- By default the speed is calculated from how far you actually moved, so vertical movement (jumps, falls, launches) counts too.
- Optionally shows the game's own velocity value instead.
- Hidden while you are dead, while the health bar is hidden, and on the mission results screen.

## Similar mods

[Speedometer](https://thunderstore.io/c/mycopunk/p/Sparroh/Speedometer/) also shows your movement speed, and its position can be changed. It reads the game's movement velocity, which players report only updates while you are on the ground. Speed Monitor measures how far you actually move, so it also shows your speed in the air: gliding, falling and launches.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/f1w3.mycopunk.speedmonitor.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `Display.UsePositionSpeed` \* | true | Calculate speed from position changes. Off: show the player's velocity from the game instead |
| `Display.IncludeVerticalSpeed` \* | true | Include vertical movement. Off: show horizontal speed only, for either calculation method |

## Multiplayer

Client-side only. It only adds text to your own HUD, so other players don't need it.

## Notes

- Speed from position changes includes every kind of movement, such as being moved by something else. The game's velocity value may differ from it.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: f1w3
Not affiliated with Pigeons at Play or Devolver Digital.
