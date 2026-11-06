# Throw Preview

Draws the arc of what you are about to throw. For grenades, hold the grenade key to aim with a dashed arc and a marker where it will explode, then release to throw. For held objects (a downed teammate's parts, barrels, rocks and so on), the arc and landing point show while you charge the throw.

## Features

- **Grenades: hold to aim, release to throw.** The game normally throws the moment you press the key. With this mod, holding the key shows the arc; releasing it throws. A quick tap still throws almost at once.
- The arc uses the game's own projectile step, so gravity, bounces off walls and range match the real grenade. A marker shows where it will go off.
- The throw itself (and the throw animation other players see) is the game's own, started when you release the key.
- **Held objects:** while you charge a throw, the arc and landing point follow the charge.
- **Revive hint (experimental):** when a thrown teammate part would join another of that teammate's parts (which revives them), the arc turns green and stops where they would connect. It follows the game's own rule (part centres closer than 2 m), but has not yet been checked during a real revive.
- Dashed line with adjustable colour, thickness and dash length.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.throwpreview.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core) and take effect at once. For colours, the MODS tab picks the hue; the opacity stays as in the config.

| Setting | Default | What it does |
|---|---|---|
| `Grenade.HoldToAim` \* | true | Hold the grenade key to see the arc, release to throw |
| `Holdables.ShowArc` \* | true | Show the arc while charging a throw of a held object |
| `Style.Color` \* | white, 75% | Arc colour |
| `Style.ReviveColor` \* | green, 90% | Arc colour when a thrown teammate part would revive them |
| `Style.Thickness` | 3 | Arc thickness, in HUD units |
| `Style.DashLength` | 12 | Length of each dash, in HUD units |
| `Style.GapLength` | 8 | Gap between dashes, in HUD units (0 = solid line) |

## Multiplayer

Client-side only. The arc is drawn only on your screen, and the throw is the game's own, so other players don't need the mod.

## Notes

- Random spread is not included in the arc.
- Where a grenade leaves your hand is only known after it has been thrown once, so the first throw of a grenade type you have never thrown uses an estimated hand position and can be slightly off. After that, the position is remembered per grenade type in `BepInEx/ThrowPreview/fire-offsets.tsv`, also across launches.
- When an upgrade gives the grenade key its own hold action, the grenade keeps the game's behaviour (throw on press).
- The aim can jump when what's under your crosshair switches between near and far. The real throw does the same, so the arc follows it.
- The Photon Disc has a wavy look in flight; the arc shows its actual path, so the two can look different.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
