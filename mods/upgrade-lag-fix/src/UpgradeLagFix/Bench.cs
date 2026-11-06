using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Pigeon.Movement;

namespace UpgradeLagFix
{
    /// <summary>
    /// A/B benchmark of the hot path: walking the equipped upgrades of each local gear with
    /// EquippedUpgradeEnumerator (what GetStackingModifier / GetAppliedCount do on every call).
    /// It only reads, the same way the game does on every walk. Each walk is run with the cache
    /// off and then on, and the two walks must return the same instances in the same order.
    /// </summary>
    internal static class Bench
    {
        private const double TargetMsPerMode = 300.0;
        private const int MaxReps = 500;

        private static readonly AccessTools.FieldRef<PlayerData, Dictionary<int, List<UpgradeInfo>>> DataRef =
            AccessTools.FieldRefAccess<PlayerData, Dictionary<int, List<UpgradeInfo>>>("data");

        public static void Run()
        {
            var log = Plugin.Log;
            var player = Player.LocalPlayer;
            if (player == null) { log.LogWarning("[bench] No local player yet. Load into the HUB first."); return; }
            var gears = player.Gear;
            if (gears == null) { log.LogWarning("[bench] Player has no gear array."); return; }

            int totalInstances = CountAllInstances(out int totalInfos);
            log.LogInfo("[bench] start. upgrade types in data: " + totalInfos + ", instances (U): " + totalInstances +
                ", cleanup-skip available: " + CleanupMemo.Available + " (per-walk times below)");

            var prevOverride = Plugin.CacheOverride;
            var prevVerify = Plugin.VerifyOverride;
            var prevCleanup = Plugin.CleanupOverride;
            var prevPrepare = Plugin.PrepareOverride;
            var prevSort = Plugin.SortOverride;
            Plugin.VerifyOverride = false;
            try
            {
                foreach (var gear in gears)
                {
                    if (gear == null) continue;
                    IUpgradable prefab;
                    try { prefab = gear.Prefab; } catch { continue; }
                    if (prefab == null) continue;
                    RunGear(prefab);
                }
            }
            catch (Exception e)
            {
                log.LogError("[bench] failed: " + e);
            }
            finally
            {
                Plugin.CacheOverride = prevOverride;
                Plugin.VerifyOverride = prevVerify;
                Plugin.CleanupOverride = prevCleanup;
                Plugin.PrepareOverride = prevPrepare;
                Plugin.SortOverride = prevSort;
            }
            log.LogInfo("[bench] done.");
        }

        private static void RunGear(IUpgradable prefab)
        {
            var log = Plugin.Log;
            string name = Patches.GearName(prefab);

            SetMode(cache: false, cleanup: false, walk: false);
            var baseIds = Walk(prefab);
            if (baseIds.Count == 0) return;

            // Calibrate the repetition count on the slow path.
            long t0 = Stopwatch.GetTimestamp();
            Walk(prefab);
            double oneMs = (Stopwatch.GetTimestamp() - t0) * Stats.TicksToMs;
            int reps = (int)Math.Max(1, Math.Min(MaxReps, Math.Ceiling(TargetMsPerMode / Math.Max(oneMs, 1e-3))));

            Measure(prefab, reps, out double offMs, out long offLookups);

            SetMode(cache: true, cleanup: false, walk: false);
            InstanceCache.Clear();
            bool sameCache = SameSequence(baseIds, Walk(prefab)); // warm-up, also builds the index
            Measure(prefab, reps, out double cacheMs, out long cacheLookups);

            SetMode(cache: true, cleanup: true, walk: false);
            CleanupMemo.Clear();
            bool sameBoth = SameSequence(baseIds, Walk(prefab));
            Measure(prefab, reps, out double bothMs, out long bothLookups);

            SetMode(cache: true, cleanup: true, walk: true);
            CleanupMemo.Clear();
            bool sameAll = SameSequence(baseIds, Walk(prefab));
            Measure(prefab, reps, out double allMs, out long allLookups);

            log.LogInfo(string.Format(
                "[bench] {0}: n={1}, reps={2} | OFF {3:F3} ms ({4} lookups) | cache {5:F4} ms ({6}) x{7:F1} | +cleanup {8:F4} ms ({9}) x{10:F1} | +prepare+sort {11:F4} ms ({12}) x{13:F1} | same order={14}",
                name, baseIds.Count, reps,
                offMs, offLookups,
                cacheMs, cacheLookups, Ratio(offMs, cacheMs),
                bothMs, bothLookups, Ratio(offMs, bothMs),
                allMs, allLookups, Ratio(offMs, allMs),
                sameCache && sameBoth && sameAll));
            if (!(sameCache && sameBoth && sameAll))
                log.LogError("[bench] " + name + ": sequence differs between modes! base=[" + string.Join(",", baseIds) + "]");
        }

        private static void SetMode(bool cache, bool cleanup, bool walk)
        {
            Plugin.CacheOverride = cache;
            Plugin.CleanupOverride = cleanup;
            Plugin.PrepareOverride = walk;
            Plugin.SortOverride = walk;
        }

        private static double Ratio(double a, double b) => b > 0 ? a / b : double.PositiveInfinity;

        private static void Measure(IUpgradable prefab, int reps, out double msPerWalk, out long lookupsPerWalk)
        {
            long l0 = Stats.Lookups;
            long t0 = Stopwatch.GetTimestamp();
            for (int r = 0; r < reps; r++) WalkCount(prefab);
            long dt = Stopwatch.GetTimestamp() - t0;
            msPerWalk = dt * Stats.TicksToMs / reps;
            lookupsPerWalk = (Stats.Lookups - l0) / reps;
        }

        private static List<int> Walk(IUpgradable prefab)
        {
            var ids = new List<int>();
            var en = new PlayerData.EquippedUpgradeEnumerator(prefab, true);
            while (en.MoveNext())
            {
                var u = en.Upgrade;
                ids.Add(u == null ? int.MinValue : u.InstanceID);
            }
            return ids;
        }

        private static int _sink;

        private static void WalkCount(IUpgradable prefab)
        {
            var en = new PlayerData.EquippedUpgradeEnumerator(prefab, true);
            while (en.MoveNext())
            {
                var u = en.Upgrade;
                if (u != null) _sink ^= u.InstanceID;
            }
        }

        private static bool SameSequence(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static int CountAllInstances(out int infos)
        {
            infos = 0;
            int total = 0;
            var data = DataRef(PlayerData.Instance);
            if (data == null) return 0;
            foreach (var kv in data)
            {
                if (kv.Value == null) continue;
                foreach (var info in kv.Value)
                {
                    infos++;
                    if (info?.Instances != null) total += info.Instances.Count;
                }
            }
            return total;
        }
    }
}
