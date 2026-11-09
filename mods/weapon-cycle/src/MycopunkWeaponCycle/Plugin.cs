using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkWeaponCycle
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // Client-side and not a sandbox mod: it only changes which slot the local player's own keys pick.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.weaponcycle";
        public const string Name = "Mycopunk Weapon Cycle";
        public const string Version = "0.2.3";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SkipEmpty;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Put the stored heavy weapon into the weapon cycle: the scroll wheel goes weapon 1 → weapon 2 → heavy, " +
                "and each press of the weapon 1 key does the same. Weapon 2 key unchanged. Takes effect at once.",
                null, MenuText.Label("cycle.heavy")));
            SkipEmpty = Config.Bind("General", "SkipEmptyOnWheel", true, new ConfigDescription(
                "The scroll wheel skips a weapon whose magazine and reserve are both empty (not one that fires without " +
                "ammo). When every other weapon is empty it stays on the current weapon. The weapon keys are unchanged. Takes effect at once.",
                null, MenuText.Label("cycle.skip_empty")));

            // Each part on its own, so one failing (game update) does not take the others down.
            _harmony = new Harmony(Guid);
            ModSetup.Try(Log, "weapon cycle", () =>
            {
                _harmony.CreateClassProcessor(typeof(SlotPatches.WheelPatch)).Patch();
                _harmony.CreateClassProcessor(typeof(SlotPatches.Weapon1Patch)).Patch();
            });
            ModSetup.Try(Log, "MODS tab", () => ModsMenu.Install(_harmony, this));
            ModSetup.Try(Log, "hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (enabled=" + Enabled.Value + ").");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
