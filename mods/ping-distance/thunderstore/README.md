# Ping Distance

Shows how far away each on-screen ping is. A distance like `123m` appears under your pings, your teammates' pings, pins placed on the map, and objective markers.

## Features

- Distance in metres under every on-screen ping, measured from your camera.
- Works for your own and teammates' pings, pins placed on the map, and the objective markers in first-person view.
- White text by default, which reads better on most ground than the ping colour; optionally in the ping's colour.
- The distance fades with the ping, and is hidden when the ping is very close.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.pingdistance.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `Pings.ShowDistance` \* | true | Show the distance under every on-screen ping |
| `Pings.MinDistance` | 3 | Hide the distance when the ping is closer than this many metres |
| `Pings.FontSize` | 18 | Font size of the distance text, in HUD units |
| `Style.UsePingColor` \* | false | Draw the distance in the ping's colour instead of white |

## Multiplayer

Client-side only. It only adds text to your own screen, so other players don't need it.

## Notes

- Markers that only appear on the map (with no on-screen marker in first-person view) don't get a distance.
- It doesn't touch your save or the game's settings file.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
