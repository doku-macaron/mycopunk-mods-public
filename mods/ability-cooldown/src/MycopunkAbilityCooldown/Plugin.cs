using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkAbilityCooldown
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.ModKit.Guid)]
    // Client-side and not a sandbox mod: it only adds text to the local HUD.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.abilitycooldown";
        public const string Name = "Mycopunk Ability Cooldown";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> FontSize;
        internal static ConfigEntry<float> ShadeAlpha;
        internal static ConfigEntry<float> SmallFontSize;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Show the seconds until the next charge over each ability icon (grenade, movement ability...). " +
                "Takes effect at once.",
                null, MenuText.Label("ability.cooldown")));
            FontSize = Config.Bind("General", "FontSize", 22f, "Font size of the seconds, in HUD units.");
            ShadeAlpha = Config.Bind("General", "ShadeAlpha", 0.8f,
                "How dark the shade over the icon is while it recharges (0 = none, 1 = black), so the number stays readable.");
            SmallFontSize = Config.Bind("General", "SmallFontSize", 13f,
                "Font size of the corner tags (the charge count, and the seconds while charges are left), in HUD units.");

            // Each part on its own, so one failing (game update) does not take the others down.
            var harmony = new Harmony(Guid);
            Try("cooldown labels", () => CooldownLabel.Install(harmony));
            Try("MODS tab", () => ModsMenu.Install(harmony, this));
            Try("hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (enabled=" + Enabled.Value + ").");
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
