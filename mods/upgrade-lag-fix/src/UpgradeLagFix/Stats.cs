using System.Diagnostics;
using System.Text;

namespace UpgradeLagFix
{
    /// <summary>Plain counters. Everything runs on the Unity main thread.</summary>
    internal static class Stats
    {
        public static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        // GetUpgradeInstanceFromID
        public static long Lookups;
        public static long LookupTicks;
        public static long CacheHits;
        public static long OriginalCalls;
        public static long OriginalNull;
        public static long CacheStale;
        public static long CacheRebuilds;
        public static long CacheResets;
        public static long VerifyChecks;
        public static long VerifyMismatches;

        // Hot callers
        public static long StackingCalls;
        public static long StackingTicks;
        public static long AppliedCountCalls;
        public static long AppliedCountTicks;
        public static long EnumMoveNext;
        public static long GearDataCleanup;
        public static long CleanupSkipped;
        public static long CleanupRun;
        public static long CleanupVerifyChecks;
        public static long CleanupVerifyMismatches;
        public static long GenBumps;
        public static long PrepGenBumps;
        public static long InfoSortUnchanged;
        public static long InfoSortHarmless;
        public static long PrepareSkipped;
        public static long PrepareRun;
        public static long PrepareVerifyChecks;
        public static long PrepareVerifyMismatches;
        public static long SortSkipped;
        public static long SortRun;
        public static long SortVerifyChecks;
        public static long SortVerifyMismatches;
        public static long StackFast;
        public static long StackRun;
        public static long StackBuilds;
        public static long StackBuildFailed;
        public static long StackVerifyChecks;
        public static long StackVerifyMismatches;

        // Top-level operations
        public static long ApplyCalls;
        public static long ApplyTicks;
        public static long ApplyMaxTicks;
        public static long ReapplyCalls;
        public static long ReapplyTicks;
        public static long ReapplyMaxTicks;

        public static bool HasApplyActivity => ApplyCalls > 0 || ReapplyCalls > 0 || StackFast > 0 || StackRun > 0;

        public static void Reset()
        {
            Lookups = LookupTicks = CacheHits = OriginalCalls = OriginalNull = 0;
            CacheStale = CacheRebuilds = CacheResets = VerifyChecks = VerifyMismatches = 0;
            StackingCalls = StackingTicks = AppliedCountCalls = AppliedCountTicks = 0;
            EnumMoveNext = GearDataCleanup = 0;
            CleanupSkipped = CleanupRun = CleanupVerifyChecks = CleanupVerifyMismatches = GenBumps = 0;
            PrepGenBumps = InfoSortUnchanged = InfoSortHarmless = PrepareSkipped = PrepareRun = PrepareVerifyChecks = PrepareVerifyMismatches = 0;
            SortSkipped = SortRun = SortVerifyChecks = SortVerifyMismatches = 0;
            StackFast = StackRun = StackBuilds = StackBuildFailed = StackVerifyChecks = StackVerifyMismatches = 0;
            ApplyCalls = ApplyTicks = ApplyMaxTicks = 0;
            ReapplyCalls = ReapplyTicks = ReapplyMaxTicks = 0;
            CleanupMemo.ResetSiteCounts();
        }

        public static bool HasActivity => Lookups != 0 || ApplyCalls != 0 || ReapplyCalls != 0 || StackingCalls != 0;

        public static string Summary(string title)
        {
            var sb = new StringBuilder();
            sb.Append("=== ").Append(title).Append(" (").Append(Plugin.ModeString)
              .Append(Patches.Detailed ? ", detailed" : ", detailed profiling off: per-call times/counts are 0").AppendLine(") ===");
            sb.AppendFormat("  ReapplyUpgrades : {0} calls, total {1:F2} ms, max {2:F2} ms", ReapplyCalls, ReapplyTicks * TicksToMs, ReapplyMaxTicks * TicksToMs).AppendLine();
            sb.AppendFormat("  ApplyUpgrades   : {0} calls, total {1:F2} ms, max {2:F2} ms", ApplyCalls, ApplyTicks * TicksToMs, ApplyMaxTicks * TicksToMs).AppendLine();
            sb.AppendFormat("  GetStackingModifier : {0} calls, {1:F2} ms", StackingCalls, StackingTicks * TicksToMs).AppendLine();
            sb.AppendFormat("  GetAppliedCount     : {0} calls, {1:F2} ms", AppliedCountCalls, AppliedCountTicks * TicksToMs).AppendLine();
            sb.AppendFormat("  EquippedUpgradeEnumerator.MoveNext : {0}, GearData.Cleanup : {1} (skipped {2}, ran tracked {3}), generation bumps {4}",
                EnumMoveNext, GearDataCleanup, CleanupSkipped, CleanupRun, GenBumps).AppendLine();
            sb.AppendFormat("  PrepareUpgrades destroyed-check : skipped {0}, ran tracked {1} | Sort : skipped {2}, ran {3} | prepare-only bumps {4} (List<UpgradeInfo>.Sort with no change: {5}, reordered but unobservable: {6})",
                PrepareSkipped, PrepareRun, SortSkipped, SortRun, PrepGenBumps, InfoSortUnchanged, InfoSortHarmless).AppendLine();
            sb.AppendFormat("  stacking table : answered {0}, original ran {1}, builds {2} (failed {3})",
                StackFast, StackRun, StackBuilds, StackBuildFailed).AppendLine();
            sb.AppendFormat("  GetUpgradeInstanceFromID : {0} calls, {1:F2} ms (hits {2}, original {3}, original-null {4})",
                Lookups, LookupTicks * TicksToMs, CacheHits, OriginalCalls, OriginalNull).AppendLine();
            sb.AppendFormat("  cache: indexed {0}, bypass {1}, duplicates {2}, stale {3}, rebuilds {4}, resets {5}, incomplete={6}",
                InstanceCache.IndexedCount, InstanceCache.BypassCount, InstanceCache.DuplicateCount,
                CacheStale, CacheRebuilds, CacheResets, InstanceCache.LastRebuildIncomplete).AppendLine();
            if (GenBumps > 0 || PrepGenBumps > 0)
                sb.Append("  bump sites: ").AppendLine(CleanupMemo.TopSites(int.MaxValue));
            if (VerifyChecks > 0 || CleanupVerifyChecks > 0 || PrepareVerifyChecks > 0 || SortVerifyChecks > 0 || StackVerifyChecks > 0)
                sb.AppendFormat("  verify: lookup {0} checks, {1} mismatches | cleanup {2}/{3} | prepare {4}/{5} | sort {6}/{7} | stacking {8}/{9} (checks/mismatches)",
                    VerifyChecks, VerifyMismatches, CleanupVerifyChecks, CleanupVerifyMismatches,
                    PrepareVerifyChecks, PrepareVerifyMismatches, SortVerifyChecks, SortVerifyMismatches,
                    StackVerifyChecks, StackVerifyMismatches).AppendLine();
            return sb.ToString().TrimEnd();
        }
    }
}
