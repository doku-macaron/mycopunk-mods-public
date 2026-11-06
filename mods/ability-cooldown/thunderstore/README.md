# Ability Cooldown

Shows the cooldown of your abilities as seconds on their icons at the bottom of the screen (grenade, movement ability and so on), the way many hero shooters and MOBAs do. The game only shows a charge bar; this mod adds the number.

## Features

- Seconds until the next charge on each ability icon: whole seconds from 10 up (`12`), one decimal below 10 (`4.2`).
- While an ability recharges, the icon is darkened (same shape as the icon box) so the number stays readable.
- Hidden when the ability is fully charged.
- Abilities with several charges (for example the Photon Disc):
  - no charges left: darkened icon with large seconds in the middle, as for a single-charge ability,
  - charges left but still recharging: not darkened, small seconds in the bottom-right corner,
  - the current number of charges in the top-right corner.
- The seconds come from how fast the charge has actually been filling, so cooldown upgrades and pauses (for example, a grenade doesn't recharge during the throw animation) are taken into account.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Macaron Core (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.mycopunk.abilitycooldown.cfg`. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Macaron Core) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` \* | true | Show the seconds until the next charge over each ability icon |
| `General.FontSize` | 22 | Font size of the seconds, in HUD units |
| `General.SmallFontSize` | 13 | Font size of the corner tags (charge count, and seconds while charges are left) |
| `General.ShadeAlpha` | 0.8 | How dark the icon gets while it recharges (0 = none, 1 = black) |

## Multiplayer

Client-side only. It only adds text to your own HUD, so other players don't need it.

## Notes

- Abilities that don't recharge over time (for example, ones that charge from kills) show no seconds, and neither does an ability whose charge has stopped filling.
- The number appears a moment after the recharge starts, once the mod has measured how fast it fills.
- It doesn't touch your save or the game's settings file.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
