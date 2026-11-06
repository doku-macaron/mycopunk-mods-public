# Quick Timers

Shortens fixed waits in Mycopunk. By default it only skips the startup animations, so the title screen appears while the game's sound loads in the background. Optional settings, off by default, also shorten the fixed delays around a mission: the drop on fixed maps, the wait before the mission starts, the wait after it ends, and the extraction flight.

## Features

- **Faster startup** (on by default): skips the startup logos and intro animations and shows the title while sound banks load.
- **Shorter mission waits** (experimental, each off by default):
  - Drop on fixed maps: the fixed 6-second drop delay becomes almost instant. Terrain generation is still waited for.
  - Mission start: the fixed wait after everyone has loaded becomes almost instant.
  - Mission end: the 3.5-second wait before rewards becomes almost instant, but only once the host has confirmed everyone's mission statistics.
  - Extraction: the return flight is cut to at most 1 second, but only when the host has received the mission statistics and every connected player is alive inside the drop pod.
- Whenever a condition can't be confirmed, or the game code has changed after an update, the original timing is kept.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/f1w3.mycopunk.quicktimers.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core).

| Setting | Default | What it does |
|---|---|---|
| `Startup.ShortenLogos` \* | true | Skip startup animations and show the title while sound banks load. Takes effect the next time the game starts |
| `Mission.ShortenDrop` \* | false | Shorten the fixed-map drop delay. Terrain generation is still awaited |
| `Mission.ShortenStart` \* | false | Shorten the delay after all players have loaded |
| `Mission.ShortenEnd` \* | false | Shorten your own wait after the host confirms all mission statistics |
| `Mission.ShortenExtraction` \* | false | Shorten the return flight when every connected player is alive inside the pod |

## Multiplayer

- `Startup.ShortenLogos` and `Mission.ShortenEnd` only affect your own game.
- `Mission.ShortenDrop`, `Mission.ShortenStart` and `Mission.ShortenExtraction` are decided by the **host**: when the host turns them on, the whole lobby gets the shorter wait, and other players don't need the mod. Turning them on while you are a client in someone else's lobby does not change those waits.
- If you host, keep in mind that these options change the pacing for everyone in your lobby.

## Notes

- With the faster startup, a sound that would play before the sound banks have finished loading may not be heard.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: f1w3
Not affiliated with Pigeons at Play or Devolver Digital.
