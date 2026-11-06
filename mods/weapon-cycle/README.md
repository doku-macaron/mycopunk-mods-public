# Weapon Cycle

Puts the heavy weapon you picked up during a mission into the weapon cycle. In the unmodded game, the scroll wheel and the weapon 1 key only switch between weapon 1 and weapon 2, so the heavy weapon needs its own key. With this mod, both include it. The wheel can also skip weapons that are completely out of ammo.

## Features

| Input | Unmodded | With this mod |
|---|---|---|
| Scroll wheel forward | Weapon 1 ⇄ weapon 2 | Weapon 1 → weapon 2 → heavy weapon → weapon 1 |
| Scroll wheel back | Weapon 1 ⇄ weapon 2 | The same, in reverse |
| Weapon 1 key | Weapon 2 if you hold weapon 1, otherwise weapon 1 | Each press: weapon 1 → weapon 2 → heavy weapon → weapon 1 (from any other slot: weapon 1) |
| Weapon 2 key | Unchanged | Unchanged |

- Without a heavy weapon, switching works as in the unmodded game.
- **Skip empty weapons on the wheel:** the wheel skips a weapon whose magazine and reserve are both empty. Weapons that can fire without ammo are never skipped. If every other weapon is empty, you stay on the current one.
- The weapon keys never skip, so you can still pick an empty weapon on purpose.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.weaponcycle.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Mod Kit) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` \* | true | Include the heavy weapon in the scroll wheel and weapon 1 key cycle. Off: the game's behaviour |
| `General.SkipEmptyOnWheel` \* | true | The scroll wheel skips weapons whose magazine and reserve are both empty |

## Multiplayer

Client-side only. It only changes which slot your own inputs switch to, using the game's normal weapon switch, so other players don't need it.

## Notes

- While you hold your utility item or grenade, the wheel switches as usual (no skipping).
- It doesn't touch your save.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
