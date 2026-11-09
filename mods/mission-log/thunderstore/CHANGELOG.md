# Changelog

## 0.1.2

- When the log file cannot be written for a moment, the row is kept and written again every 5 seconds and when the game quits. A row still not written when the game quits is lost.
- Code follows the Mycopunk modding wiki conventions (publicized game assembly, `[HarmonyPatch]` classes, no reflection by name).
- Needs doku_macaron-MacaronCore 1.1.0.

## 0.1.1

- Depends on doku_macaron-MacaronCore (the renamed shared library) instead of doku_macaron-ModKit. No other change in game.

## 0.1.0

- First release on Thunderstore.
