# Changelog

## 0.1.1

- New option `IncludeVerticalSpeed` (on by default): turn it off to show horizontal speed only.
- Position speed uses the distance moved over about 0.5 seconds and is smoothed: stopping shows at once, speeding up and slowing down show slightly later.
- Code follows the Mycopunk modding wiki conventions (publicized game assembly, `[HarmonyPatch]` classes, no reflection by name).
- Depends on doku_macaron-MacaronCore 1.1.0 (the renamed shared library) instead of doku_macaron-ModKit.
- README: names the similar mod (Speedometer) and what this one adds (air and glide speed).

## 0.1.0

- First release on Thunderstore.
