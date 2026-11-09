using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkQuickTimers
{
    /// <summary>
    /// Shorter fixed waits: the startup animations (title shown while the sound banks load) and, off by default,
    /// the mission drop, start, end and extraction delays. Split out of the perf mod (2026-11-06); the code is
    /// f1w3's.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // The mission timing options can affect the whole lobby when enabled by the host.
    // Without this attribute the game treats any BepInEx plugin as a sandbox mod.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "f1w3.mycopunk.quicktimers";
        public const string Name = "Mycopunk Quick Timers";
        public const string Version = "0.1.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> FastDrop, FastMissionStart, FastMissionEnd, FastExtraction;
        private static ConfigEntry<bool> _fastStartup;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _fastStartup = Config.Bind("Startup", "ShortenLogos", true, new ConfigDescription(
                "Skip startup animations and show the title while sound banks load. Changes take effect the next time the game starts.",
                null, MenuText.Label("quick.startup")));
            FastDrop = Config.Bind("Mission", "ShortenDrop", false, new ConfigDescription(
                "Shorten the default fixed-map drop delay. The host controls this for the lobby; terrain generation is still awaited.",
                null, MenuText.Label("quick.drop")));
            FastExtraction = Config.Bind("Mission", "ShortenExtraction", false, new ConfigDescription(
                "Shorten the return flight when the host has received mission statistics and all connected players are alive inside the pod. Otherwise retain the original duration.",
                null, MenuText.Label("quick.extraction")));
            FastMissionStart = Config.Bind("Mission", "ShortenStart", false, new ConfigDescription(
                "Shorten the delay after players have loaded. The host controls this for the lobby.",
                null, MenuText.Label("quick.mission_start")));
            FastMissionEnd = Config.Bind("Mission", "ShortenEnd", false, new ConfigDescription(
                "Shorten the local delay after the host confirms all mission statistics. If confirmation is unavailable, retain the original delay.",
                null, MenuText.Label("quick.mission_end")));

            _harmony = new Harmony(Guid);
            StartupTiming.Setup(_harmony);
            MissionDelays.Setup(_harmony);
            if (_fastStartup.Value) StartupLogos.Setup(_harmony);
            Log.LogInfo(Name + " " + Version + " loaded (startup=" + _fastStartup.Value + ", drop=" + FastDrop.Value +
                ", start=" + FastMissionStart.Value + ", end=" + FastMissionEnd.Value + ", extraction=" + FastExtraction.Value + ").");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
