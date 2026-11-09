using System;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UpgradeLagFix
{
    /// <summary>
    /// Keeps this plugin's log lines in a per-session file (LogOutput.log is overwritten on every
    /// launch) and dumps the stats automatically: every N seconds of real time, on scene changes,
    /// when an Ouroboros room is completed, and on quit. A dump is skipped when no apply ran since
    /// the last one, and each dump resets the counters like F11 does.
    /// </summary>
    internal static class SessionLog
    {
        private static SessionLogFile _file;
        private static float _intervalSeconds;
        private static bool _onEvents;
        private static float _nextDump;
        private static string _pendingReason;

        public static string FilePath => _file?.FilePath;

        public static void Setup(Harmony harmony, ManualLogSource log, bool toFile, float intervalSeconds, bool onEvents)
        {
            _intervalSeconds = intervalSeconds;
            _onEvents = onEvents;
            _nextDump = intervalSeconds > 0 ? Time.realtimeSinceStartup + intervalSeconds : float.MaxValue;

            if (toFile)
                _file = SessionLogFile.Open(log, "UpgradeLagFix", "perf");

            if (onEvents)
            {
                SceneManager.activeSceneChanged += ActiveSceneChanged;
                try
                {
                    harmony.CreateClassProcessor(typeof(RoomCompletedPatch)).Patch();
                }
                catch (Exception e)
                {
                    log.LogWarning("Could not hook OuroMission.OnRoomCompleted: " + e.Message);
                }
            }
        }

        [HarmonyPatch(typeof(OuroMission), nameof(OuroMission.OnRoomCompleted))]
        private static class RoomCompletedPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => Request("ouroboros room completed");
        }

        private static void ActiveSceneChanged(Scene from, Scene to) => Request("scene " + from.name + " -> " + to.name);

        // Dumping is deferred to the next Update so it never runs inside a game method.
        private static void Request(string reason)
        {
            if (!_onEvents) return;
            _pendingReason = _pendingReason == null ? reason : _pendingReason + "; " + reason;
        }

        /// <summary>Called from Plugin.Update every frame.</summary>
        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            string title;
            if (_pendingReason != null)
            {
                title = "auto stats: " + _pendingReason;
                _pendingReason = null;
            }
            else if (now >= _nextDump)
            {
                title = "auto stats: every " + _intervalSeconds.ToString("0") + " s";
            }
            else return;

            Dump(title);
            if (_intervalSeconds > 0) _nextDump = now + _intervalSeconds;
        }

        public static void OnQuit()
        {
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            Dump("auto stats: quit");
            _file?.Dispose();
            _file = null;
        }

        private static void Dump(string title)
        {
            if (!Stats.HasApplyActivity) return;
            Plugin.Log.LogInfo(Stats.Summary(title));
            Stats.Reset();
        }
    }
}
