using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;
using UnityEngine.InputSystem;

namespace UpgradeLagFix
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid)]
    // Client-side, no gameplay changes (same stats, same apply order, only faster lookups).
    // Without this attribute the game treats any BepInEx plugin as a sandbox mod.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.UpgradeLagFix";
        public const string Name = "Upgrade Lag Fix";
        public const string Version = "1.1.1";

        internal static ManualLogSource Log;

        private static ConfigEntry<bool> _cacheEnabled;
        private static ConfigEntry<bool> _verify;
        private static ConfigEntry<bool> _cleanupMemo;
        private static ConfigEntry<bool> _prepareMemo;
        private static ConfigEntry<bool> _sortSkip;
        private static ConfigEntry<bool> _stackTable;
        private static ConfigEntry<float> _logThresholdMs;
        private static ConfigEntry<bool> _hotkeys;
        private static ConfigEntry<bool> _detailedProfiling;
        private static ConfigEntry<bool> _sessionFile;
        private static ConfigEntry<float> _dumpSeconds;
        private static ConfigEntry<bool> _dumpOnEvents;
        private static bool _cacheFaulted;

        // Temporary overrides used by the benchmark.
        internal static bool? CacheOverride;
        internal static bool? VerifyOverride;
        internal static bool? CleanupOverride;
        internal static bool? PrepareOverride;
        internal static bool? SortOverride;

        internal static bool CacheActive => !_cacheFaulted && (CacheOverride ?? _cacheEnabled.Value);
        internal static bool VerifyActive => VerifyOverride ?? _verify.Value;
        internal static bool CleanupMemoActive => CleanupMemo.Available && (CleanupOverride ?? _cleanupMemo.Value);
        internal static bool PrepareMemoActive => PrepareMemo.PrepareAvailable && (PrepareOverride ?? _prepareMemo.Value);
        internal static bool SortSkipActive => PrepareMemo.SortAvailable && (SortOverride ?? _sortSkip.Value);
        internal static bool StackTableActive => StackingMemo.Available && PrepareMemoActive && CleanupMemoActive && _stackTable.Value;
        internal static string ModeString =>
            "cache=" + OnOff(CacheActive, true) +
            ", cleanup-skip=" + OnOff(CleanupMemoActive, CleanupMemo.Available) +
            ", prepare-skip=" + OnOff(PrepareMemoActive, PrepareMemo.PrepareAvailable) +
            ", sort-skip=" + OnOff(SortSkipActive, PrepareMemo.SortAvailable) +
            ", stack-table=" + OnOff(StackTableActive, StackingMemo.Available) +
            (VerifyActive ? ", verify" : "");

        private static string OnOff(bool active, bool available) => active ? "ON" : available ? "OFF" : "N/A";
        internal static double LogThresholdMs => _logThresholdMs.Value;

        private void Awake()
        {
            Log = Logger;

            _cacheEnabled = Config.Bind("Cache", "Enabled", true, new ConfigDescription("Use the InstanceID index for PlayerData.GetUpgradeInstanceFromID. Toggle in game with F9.",
                null, MenuText.Label("perf.cache")));
            _verify = Config.Bind("Cache", "Verify", false, new ConfigDescription("Debug: run the original lookup on every call, compare it with the cache, and log mismatches. " +
                "The original result is always used, so this is safe but slow.",
                null, MenuText.Label("perf.verify")));
            _cleanupMemo = Config.Bind("Cleanup", "SkipRedundant", true, new ConfigDescription("Skip GearData.Cleanup when it provably cannot remove anything (it runs on every step of " +
                "EquippedUpgradeEnumerator). With Cache.Verify the original Cleanup still runs and is checked. Toggle in game with F8.",
                null, MenuText.Label("perf.cleanup")));
            _prepareMemo = Config.Bind("Walk", "SkipRedundantPrepare", true, new ConfigDescription("Skip the destroyed-check in GearData.PrepareUpgrades (run at the start of every equipped-upgrade walk) " +
                "when it provably cannot remove anything. Toggle in game with F7 (together with SkipSortedSort).",
                null, MenuText.Label("perf.prepare")));
            _sortSkip = Config.Bind("Walk", "SkipSortedSort", true, new ConfigDescription("Skip equippedUpgrades.Sort() in PrepareUpgrades when the list is already strictly ascending, " +
                "where sorting cannot change it. Toggle in game with F7 (together with SkipRedundantPrepare).",
                null, MenuText.Label("perf.sort")));
            _stackTable = Config.Bind("Walk", "StackingTable", true, new ConfigDescription("Answer GetStackingModifier / GetAppliedCount (how many copies of an upgrade come before this one) " +
                "from a per-gear table instead of walking the equipped list on every call. Used only while the walk " +
                "would provably change nothing; needs SkipRedundantPrepare and Cleanup.SkipRedundant. Toggle in game with F6.",
                null, MenuText.Label("perf.stacking")));
            _logThresholdMs = Config.Bind("Profiling", "LogThresholdMs", 1.0f,
                "Log ApplyUpgrades / ReapplyUpgrades calls that take at least this many milliseconds.");
            _detailedProfiling = Config.Bind("Profiling", "Detailed", false,
                "Install per-call profiling hooks (GetStackingModifier / GetAppliedCount timing, MoveNext and Cleanup counts, " +
                "lookup timing). They add overhead to the hot path, so leave this off except when investigating. Needs a game restart.");
            _hotkeys = Config.Bind("Profiling", "Hotkeys", false, new ConfigDescription(
                "F6: toggle stacking table, F7: toggle prepare+sort skip, F8: toggle cleanup skip, F9: toggle cache (all print and reset stats). F10: run benchmark. F11: print and reset stats. " +
                "Off by default: switch these in the MODS tab of the options window instead.",
                null, MenuText.Label("perf.hotkeys")));

            _sessionFile = Config.Bind("Profiling", "SessionLogFile", true,
                "Also write this mod's log lines to BepInEx/UpgradeLagFix/logs/perf-<start time>.log, one file per launch " +
                "(LogOutput.log is overwritten on every launch). Needs a game restart.");
            _dumpSeconds = Config.Bind("Profiling", "StatsDumpSeconds", 120f,
                "Print and reset the stats (like F11) every this many seconds of real time. 0 turns it off. " +
                "Nothing is printed when no ApplyUpgrades ran since the last dump. Needs a game restart.");
            _dumpOnEvents = Config.Bind("Profiling", "StatsDumpOnEvents", true,
                "Also print and reset the stats on scene changes, when an Ouroboros room is completed, and on quit. Needs a game restart.");

            _cacheEnabled.SettingChanged += (_, __) => InstanceCache.Clear();
            // From the MODS tab, the cfg or a hotkey alike: print and reset the stats and drop the cleanup memo.
            foreach (var e in new[] { _cacheEnabled, _verify, _cleanupMemo, _prepareMemo, _sortSkip, _stackTable })
                e.SettingChanged += (_, __) => OnModeChanged();

            if (_hotkeys.Value)
            {
                Hotkeys.Register(this, Key.F6, "toggle stacking table");
                Hotkeys.Register(this, Key.F7, "toggle prepare+sort skip");
                Hotkeys.Register(this, Key.F8, "toggle cleanup skip");
                Hotkeys.Register(this, Key.F9, "toggle cache");
                Hotkeys.Register(this, Key.F10, "benchmark");
                Hotkeys.Register(this, Key.F11, "print stats");
            }
            Hotkeys.ReportConflicts(this);

            var harmony = new Harmony(Guid);
            ModsMenu.Install(harmony, this);
            Patches.ApplyAll(harmony, _detailedProfiling.Value);
            CleanupMemo.Setup(harmony);
            PrepareMemo.Setup(harmony);
            StackingMemo.Setup(harmony);
            SessionLog.Setup(harmony, Log, _sessionFile.Value, _dumpSeconds.Value, _dumpOnEvents.Value);
            Log.LogInfo(Name + " " + Version + " loaded. " + ModeString + ", detailed profiling=" + _detailedProfiling.Value +
                (SessionLog.FilePath != null ? ", session log: " + SessionLog.FilePath : ""));
        }

        internal static void OnCacheFault(Exception e)
        {
            if (_cacheFaulted) return;
            _cacheFaulted = true;
            InstanceCache.Clear();
            Log.LogError("Cache disabled after an unexpected error; falling back to the original lookup. " + e);
        }

        private void OnApplicationQuit()
        {
            SessionLog.OnQuit();
        }

        private void OnDestroy() => SessionLog.OnQuit();

        private static void OnModeChanged()
        {
            try
            {
                Log.LogInfo(Stats.Summary("stats before toggle"));
                Stats.Reset();
                CleanupMemo.Clear();
                Log.LogInfo("Mode is now " + ModeString);
            }
            catch (Exception e)
            {
                Log.LogWarning("Mode change handling failed: " + e.Message);
            }
        }

        private void Update()
        {
            SessionLog.Tick();
            if (!_hotkeys.Value) return;
            Keyboard kb;
            try { kb = Keyboard.current; } catch { return; }
            if (kb == null) return;

            // The stats, the memo and the log line are handled by OnModeChanged (SettingChanged).
            if (kb.f6Key.wasPressedThisFrame)
            {
                _stackTable.Value = !_stackTable.Value;
            }
            else if (kb.f7Key.wasPressedThisFrame)
            {
                bool on = !_prepareMemo.Value;
                _prepareMemo.Value = on;
                _sortSkip.Value = on;
            }
            else if (kb.f8Key.wasPressedThisFrame)
            {
                _cleanupMemo.Value = !_cleanupMemo.Value;
            }
            else if (kb.f9Key.wasPressedThisFrame)
            {
                _cacheEnabled.Value = !_cacheEnabled.Value;
            }
            else if (kb.f10Key.wasPressedThisFrame)
            {
                Bench.Run();
            }
            else if (kb.f11Key.wasPressedThisFrame)
            {
                Log.LogInfo(Stats.Summary("stats"));
                Stats.Reset();
            }
        }
    }
}
