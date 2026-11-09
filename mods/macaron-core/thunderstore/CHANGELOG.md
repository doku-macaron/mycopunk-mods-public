# Changelog

## 1.1.0

- Share HUD text, health bar visibility, canvas camera selection, and mod setup error handling between mods.
- MODS tab: after a mod is reloaded in game (hot reload), its rows are rebuilt with its current settings.
- MODS tab: if hooking the options window fails, the hooks are taken back and the next mod tries again.
- Helper for mods to apply patches that only work together (all or none).
- Groundwork for movable HUD elements (not used by any mod yet).
- Code follows the Mycopunk modding wiki conventions (publicized game assembly, `[HarmonyPatch]` classes, no reflection by name).
- Mods built against 1.1.0 ask BepInEx for at least this version.

## 1.0.0

- First release under this name. Same code as doku_macaron-ModKit 1.0.0, renamed so a library used only by doku_macaron's mods does not look general. New plugin GUID `doku_macaron.mycopunk.macaroncore`.
