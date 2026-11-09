using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;

namespace MycopunkMissionLog
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // Client-side and not a sandbox mod: it only reads and writes its own file.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.missionlog";
        public const string Name = "Mycopunk Mission Log";
        public const string Version = "0.1.2";

        internal static ManualLogSource Log;
        private Harmony _harmony;
        public bool CanHotReload => !MissionRecorder.Active;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(Guid);
            MissionRecorder.Setup(_harmony);
            Hotkeys.ReportConflicts(this);
            Log.LogInfo(Name + " " + Version + " loaded. Rows go to " + MissionRecorder.FilePath);
        }

        private void Update() => MissionRecorder.Tick();

        private void OnDestroy() => _harmony?.UnpatchSelf();
        private void OnApplicationQuit() => MissionRecorder.FlushPending();
    }
}
