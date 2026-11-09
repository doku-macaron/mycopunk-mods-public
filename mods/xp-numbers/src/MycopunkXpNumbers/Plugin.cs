using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkXpNumbers
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // Client-side and not a sandbox mod: it only adds text to the local menus and reads the save.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.xpnumbers";
        public const string Name = "Mycopunk XP Numbers";
        public const string Version = "0.2.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> ResultsFontScale;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Show the XP into the current level and the XP the next level costs next to the level bars " +
                "(the class screen after TAB, the build screen of weapons, gear and classes, and the mission results). Shown from the next " +
                "time a bar is filled.",
                null, MenuText.Label("xp.level")));
            ResultsFontScale = Config.Bind("Results", "FontScale", 0.7f, new ConfigDescription(
                "Size of the numbers under the bars of the mission results, relative to the level text.",
                new AcceptableValueRange<float>(0.3f, 2f)));

            // Each part on its own, so one failing (game update) does not take the others down.
            _harmony = new Harmony(Guid);
            ModSetup.Try(Log, "XP numbers", () => _harmony.CreateClassProcessor(typeof(XpBars.DetailsPatch)).Patch());
            ModSetup.Try(Log, "XP numbers", () => _harmony.CreateClassProcessor(typeof(XpBars.SelectionPatch)).Patch());
            ModSetup.Try(Log, "XP numbers on the mission results", () => _harmony.CreateClassProcessor(typeof(XpResults.ShowXPPatch)).Patch());
            ModSetup.Try(Log, "MODS tab", () => ModsMenu.Install(_harmony, this));
            ModSetup.Try(Log, "hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (enabled=" + Enabled.Value + ").");
        }


        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            XpResults.Cleanup();
        }
    }
}
