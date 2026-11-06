using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkPingDistance
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid)]
    // Client-side and not a sandbox mod: it only adds text to the local HUD.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.pingdistance";
        public const string Name = "Mycopunk Ping Distance";
        public const string Version = "0.2.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> PingDistance;
        internal static ConfigEntry<float> MinDistance;
        internal static ConfigEntry<float> FontSize;
        internal static ConfigEntry<bool> UsePingColor;

        private void Awake()
        {
            Log = Logger;
            PingDistance = Config.Bind("Pings", "ShowDistance", true, new ConfigDescription(
                "Show the distance in metres under every on-screen ping: your and your teammates' pings, " +
                "pins placed on the map and objective markers. Takes effect at once.",
                null, MenuText.Label("ping.distance")));
            MinDistance = Config.Bind("Pings", "MinDistance", 3f,
                "Hide the distance when the ping is closer than this many metres.");
            FontSize = Config.Bind("Pings", "FontSize", 18f,
                "Font size of the distance text, in HUD units.");
            UsePingColor = Config.Bind("Style", "UsePingColor", false, new ConfigDescription(
                "Draw the distance in the ping's colour instead of white. White reads better on most ground.",
                null, MenuText.Label("ping.color")));

            // Each part on its own, so one failing (game update) does not take the others down.
            var harmony = new Harmony(Guid);
            Try("ping distance labels", () => gameObject.AddComponent<PingDistanceLabels>());
            Try("MODS tab", () => ModsMenu.Install(harmony, this));
            Try("hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (ping distance=" + PingDistance.Value + ").");
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
