using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkMissionLog
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.ModKit.Guid)]
    // Client-side and not a sandbox mod: it only reads and writes its own file.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.missionlog";
        public const string Name = "Mycopunk Mission Log";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);
            MissionRecorder.Setup(harmony);
            Hotkeys.ReportConflicts(this);
            Log.LogInfo(Name + " " + Version + " loaded. Rows go to " + MissionRecorder.FilePath);
        }

        private void Update() => MissionRecorder.Tick();
    }
}
