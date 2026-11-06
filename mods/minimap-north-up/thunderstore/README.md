# Minimap North Up

Keeps north at the top of the corner minimap. Instead of the map turning with you, your arrow turns, the same way the full-screen map already works. Icons on the minimap stay in their north-up positions too.

## Features

- The corner minimap no longer rotates; north is always up.
- Your player arrow rotates instead, on the corner minimap and while it opens into the full-screen map.
- Can be switched on and off at any time; it takes effect at once.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.minimapnorthup.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Mod Kit).

| Setting | Default | What it does |
|---|---|---|
| `Minimap.NorthUp` \* | true | Keep north at the top of the corner minimap and turn the player arrow instead of the map |

## Multiplayer

Client-side only. It only changes how your own screen looks, so other players don't need it.

## Notes

- It doesn't touch your save or the game's settings file.
- If a game update changes the minimap code so that the mod can't find what it replaces, it changes nothing and the minimap behaves as in the unmodded game (the log then shows `north-up=N/A`).

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
