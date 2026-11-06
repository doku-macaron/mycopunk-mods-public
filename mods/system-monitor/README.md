# System Monitor

Shows a small readout of your frame rate and, when you have joined someone else's game, your ping to the host: `FPS 120 / PING 45 ms`. It sits in the bottom-left corner by default and can be moved to any corner.

## Features

- FPS, averaged over about half a second.
- PING: round-trip time to the host over the Steam connection, shown only while you are connected to a multiplayer game as a client. When it can't be read, it shows `--`.
- White text on a dark box, scaled with your screen height.
- Can be placed in any of the four corners; changes take effect at once.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/f1w3.mycopunk.systemmonitor.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `Position.Top` \* | false | Show the monitor at the top of the screen instead of the bottom |
| `Position.Right` \* | false | Show the monitor on the right of the screen instead of the left |

## Multiplayer

Client-side only. It only reads your own frame rate and connection, so other players don't need it.

## Notes

- There is no ping while you are hosting or playing solo, since there is no host to measure against; only FPS is shown then.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: f1w3
Not affiliated with Pigeons at Play or Devolver Digital.
