# Changelog

## 0.3.0

- The arc is drawn in the world (3D) instead of on the HUD.
- `Thickness`, `DashLength` and `GapLength` are now in world units (Thickness: 1 = 0.01 m; DashLength and GapLength: 1 = 0.05 m).
- The arc starts from where the camera is drawn this frame, and held objects (teammate parts, barrels, rocks) use the drawn camera direction, so the arc lines up better while moving.
- If grenade aiming cannot hook every method it needs, it hooks none and grenades are thrown normally.
- Code follows the Mycopunk modding wiki conventions (publicized game assembly, `[HarmonyPatch]` classes, no reflection by name).
- Needs doku_macaron-MacaronCore 1.1.0.

## 0.2.1

- Depends on doku_macaron-MacaronCore (the renamed shared library) instead of doku_macaron-ModKit. No other change in game.
- README: the teammate-part revive colour is marked experimental (not yet checked in a real revive).

## 0.2.0

- First release on Thunderstore.
