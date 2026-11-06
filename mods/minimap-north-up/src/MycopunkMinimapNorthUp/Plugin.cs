using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkMinimapNorthUp
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.ModKit.Guid)]
    // Client-side and not a sandbox mod: it only changes how the local minimap is drawn.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.minimapnorthup";
        public const string Name = "Mycopunk Minimap North Up";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> NorthUp;

        private void Awake()
        {
            Log = Logger;
            NorthUp = Config.Bind("Minimap", "NorthUp", true, new ConfigDescription(
                "Keep north at the top of the corner minimap and turn the player arrow instead of the map " +
                "(the way the full-screen map already works). Takes effect at once.",
                null, MenuText.Label("minimap.north")));

            // Each part on its own, so one failing (game update) does not take the others down.
            var harmony = new Harmony(Guid);
            Try("north-up minimap", () => MinimapNorthUp.Install(harmony));
            Try("MODS tab", () => ModsMenu.Install(harmony, this));
            Try("hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (north-up=" + (MinimapNorthUp.Active ? NorthUp.Value.ToString() : "N/A") + ").");
        }

        private static void Try(string what, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                Log.LogError("Could not set up " + what + "; it is off: " + e);
            }
        }
    }
}
