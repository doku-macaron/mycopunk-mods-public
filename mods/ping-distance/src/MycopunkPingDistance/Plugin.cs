using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkPingDistance
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // Client-side and not a sandbox mod: it only adds text to the local HUD.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.pingdistance";
        public const string Name = "Mycopunk Ping Distance";
        public const string Version = "0.2.3";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> PingDistance;
        internal static ConfigEntry<float> MinDistance;
        internal static ConfigEntry<float> FontSize;
        internal static ConfigEntry<bool> UsePingColor;

        private Harmony _harmony;

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
                "Draw the distance in the ping's color instead of white. White reads better on most ground.",
                null, MenuText.Label("ping.color")));

            // Each part on its own, so one failing (game update) does not take the others down.
            _harmony = new Harmony(Guid);
            ModSetup.Try(Log, "ping distance labels", () => gameObject.AddComponent<PingDistanceLabels>());
            ModSetup.Try(Log, "MODS tab", () => ModsMenu.Install(_harmony, this));
            ModSetup.Try(Log, "hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (ping distance=" + PingDistance.Value + ").");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
