using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace UpgradeLagFix
{
    /// <summary>
    /// Speeds up PlayerData.GearData.PrepareUpgrades(sort), which EquippedUpgradeEnumerator's
    /// constructor runs at the start of every walk. It does two things:
    ///
    /// 1. Destroyed-check: from the end, remove entries whose GetUpgrade() is null or IsDestroyed().
    ///    Skipped when the previous check removed nothing and nothing it depends on has changed:
    ///    the equipped list (reference + List._version), PlayerData and its data dictionary, and
    ///    CleanupMemo.PrepGen. PrepGen is bumped right before every instruction in Assembly-CSharp
    ///    that removes from UpgradeInstance/UpgradeInfo/GearData/Upgrade collections, reorders a
    ///    List&lt;UpgradeInfo&gt; (GetUpgradeInfo returns the first match), or stores to the fields
    ///    IsDestroyed reads through (data, _instance, Instances, id, collectedGear, upgradeIDs,
    ///    GearData.gear, gearID, upgradeIDv2, UpgradeInfo.idV2).
    ///
    /// 2. equippedUpgrades.Sort(): UpgradeEquipData.CompareTo orders by Upgrade.Priority, APIName,
    ///    TimeEquipped and finally InstanceID, so distinct entries never compare equal and the sorted
    ///    order is unique. If the list is already strictly ascending, sorting returns it unchanged,
    ///    so the sort is skipped (n-1 comparisons instead of O(n log n)). List.Sort always increments
    ///    _version, so a skipped sort increments it too. Any non-ascending pair, equal pair or
    ///    exception falls back to the real Sort().
    /// </summary>
    internal static class PrepareMemo
    {
        private sealed class Memo
        {
            public int Gen;
            public PlayerData Pd;
            public object Data;
            public List<PlayerData.UpgradeEquipData> List;
            public int VersionAfter;
        }

        public struct State
        {
            public bool Track;
            public bool ExpectNoop;
            public int Gen;
            public List<PlayerData.UpgradeEquipData> List;
            public int CountBefore;
        }

        private static readonly ConditionalWeakTable<object, Memo> Memos = new ConditionalWeakTable<object, Memo>();

        private static AccessTools.FieldRef<object, List<PlayerData.UpgradeEquipData>> _upsRef;
        private static AccessTools.FieldRef<List<PlayerData.UpgradeEquipData>, int> _versionRef;
        private static AccessTools.FieldRef<PlayerData, Dictionary<int, List<UpgradeInfo>>> _dataRef;
        private static MethodInfo _listSort;
        private static int _mismatchLogs;
        private static int _sortMismatchLogs;
        private static int _sortSitesReplaced;

        public static bool PrepareAvailable { get; private set; }
        public static bool SortAvailable { get; private set; }

        public static void Setup(Harmony harmony)
        {
            var log = Plugin.Log;
            try
            {
                var gearData = AccessTools.Inner(typeof(PlayerData), "GearData");
                _upsRef = AccessTools.FieldRefAccess<List<PlayerData.UpgradeEquipData>>(gearData, "equippedUpgrades");
                _versionRef = AccessTools.FieldRefAccess<List<PlayerData.UpgradeEquipData>, int>("_version");
                _dataRef = AccessTools.FieldRefAccess<PlayerData, Dictionary<int, List<UpgradeInfo>>>("data");
                _listSort = AccessTools.Method(typeof(List<PlayerData.UpgradeEquipData>), "Sort", Type.EmptyTypes);

                var prepare = AccessTools.Method(gearData, "PrepareUpgrades");
                harmony.Patch(prepare, transpiler: new HarmonyMethod(typeof(PrepareMemo), nameof(Sort_Transpiler)));
                SortAvailable = _sortSitesReplaced == 1;
                if (!SortAvailable)
                    log.LogWarning("Sort skip disabled: expected 1 List.Sort() call in PrepareUpgrades, found " + _sortSitesReplaced);

                if (CleanupMemo.Available && CleanupMemo.PrepAvailable)
                {
                    harmony.Patch(prepare,
                        prefix: new HarmonyMethod(typeof(PrepareMemo), nameof(Prepare_Prefix)),
                        finalizer: new HarmonyMethod(typeof(PrepareMemo), nameof(Prepare_Finalizer)));
                    PrepareAvailable = true;
                }
                else
                {
                    log.LogWarning("Prepare memo disabled: generation tracking is not available");
                }
                log.LogInfo("Prepare memo: destroyed-check skip " + (PrepareAvailable ? "ready" : "N/A") +
                            ", sort skip " + (SortAvailable ? "ready" : "N/A"));
            }
            catch (Exception e)
            {
                PrepareAvailable = false;
                SortAvailable = false;
                log.LogError("Prepare memo setup failed, feature disabled: " + e);
            }
        }

        /// <summary>True while a remembered no-op destroyed-check is still valid for this GearData.</summary>
        internal static bool IsClean(object gearData, List<PlayerData.UpgradeEquipData> ups, PlayerData pd)
        {
            if (!Plugin.PrepareMemoActive || ups == null || pd == null) return false;
            return Memos.TryGetValue(gearData, out var m)
                && m.Gen == CleanupMemo.PrepGen
                && ReferenceEquals(m.Pd, pd)
                && ReferenceEquals(m.Data, _dataRef(pd))
                && ReferenceEquals(m.List, ups)
                && _versionRef(ups) == m.VersionAfter;
        }

        /// <summary>Call right after IsClean: the list was re-sorted in place without change.</summary>
        internal static void AdvanceVersion(object gearData, int version)
        {
            if (Memos.TryGetValue(gearData, out var m)) m.VersionAfter = version;
        }

        // ---------------- Sort ----------------

        private static IEnumerable<CodeInstruction> Sort_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var helper = AccessTools.Method(typeof(PrepareMemo), nameof(SortOrSkip));
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo mi && mi == _listSort)
                {
                    // Same stack shape: (List<UpgradeEquipData>) -> void.
                    ins.opcode = OpCodes.Call;
                    ins.operand = helper;
                    replaced++;
                }
                yield return ins;
            }
            _sortSitesReplaced = replaced;
        }

        public static void SortOrSkip(List<PlayerData.UpgradeEquipData> list)
        {
            if (!Plugin.SortSkipActive || !SortAvailable)
            {
                list.Sort();
                return;
            }

            int n = list.Count;
            bool ascending = true;
            if (n > 1)
            {
                var cmp = Comparer<PlayerData.UpgradeEquipData>.Default;
                try
                {
                    var prev = list[0];
                    for (int i = 1; i < n; i++)
                    {
                        var cur = list[i];
                        if (cmp.Compare(prev, cur) >= 0) { ascending = false; break; }
                        prev = cur;
                    }
                }
                catch
                {
                    // Let the real Sort reproduce whatever the original would do.
                    ascending = false;
                }
            }

            if (!ascending)
            {
                Stats.SortRun++;
                list.Sort();
                return;
            }

            if (Plugin.VerifyActive && n > 1) VerifySorted(list);

            _versionRef(list)++; // List<T>.Sort always increments _version.
            Stats.SortSkipped++;
        }

        private static void VerifySorted(List<PlayerData.UpgradeEquipData> list)
        {
            Stats.SortVerifyChecks++;
            var copy = new List<PlayerData.UpgradeEquipData>(list);
            copy.Sort();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var b = copy[i];
                if (a.upgradeID != b.upgradeID || a.x != b.x || a.y != b.y || a.rotation != b.rotation)
                {
                    Stats.SortVerifyMismatches++;
                    if (_sortMismatchLogs++ < 20)
                        Plugin.Log.LogError("SORT VERIFY MISMATCH at " + i + ": kept #" + a.upgradeID + ", Sort() gives #" + b.upgradeID);
                    return;
                }
            }
        }

        // ---------------- Destroyed-check ----------------

        private static bool Prepare_Prefix(object __instance, bool sort, out State __state)
        {
            __state = default;
            if (!Plugin.PrepareMemoActive) return true;

            var ups = _upsRef(__instance);
            if (ups == null) return true;
            var pd = PlayerData.Instance;
            if (pd == null) return true;

            if (Memos.TryGetValue(__instance, out var m)
                && m.Gen == CleanupMemo.PrepGen
                && ReferenceEquals(m.Pd, pd)
                && ReferenceEquals(m.Data, _dataRef(pd))
                && ReferenceEquals(m.List, ups)
                && _versionRef(ups) == m.VersionAfter)
            {
                if (!Plugin.VerifyActive)
                {
                    // The destroyed-check would remove nothing; only the sort part remains.
                    if (sort) SortOrSkip(ups);
                    m.VersionAfter = _versionRef(ups);
                    Stats.PrepareSkipped++;
                    return false;
                }
                __state.ExpectNoop = true;
            }

            __state.Track = true;
            __state.Gen = CleanupMemo.PrepGen;
            __state.List = ups;
            __state.CountBefore = ups.Count;
            return true;
        }

        private static Exception Prepare_Finalizer(Exception __exception, object __instance, State __state)
        {
            if (!__state.Track) return __exception;
            Stats.PrepareRun++;

            var ups = _upsRef(__instance);
            // The check only ever removes entries and Sort keeps the count, so an unchanged count
            // on the same list means nothing was removed.
            bool noop = __exception == null && ReferenceEquals(ups, __state.List) && ups.Count == __state.CountBefore;

            if (__state.ExpectNoop)
            {
                Stats.PrepareVerifyChecks++;
                if (!noop)
                {
                    Stats.PrepareVerifyMismatches++;
                    if (_mismatchLogs++ < 20)
                        Plugin.Log.LogError("PREPARE VERIFY MISMATCH: memo said no-op but PrepareUpgrades removed entries");
                }
            }

            Memos.Remove(__instance);
            if (noop && __state.Gen == CleanupMemo.PrepGen)
            {
                var pd = PlayerData.Instance;
                if (pd != null)
                {
                    Memos.Add(__instance, new Memo
                    {
                        Gen = __state.Gen,
                        Pd = pd,
                        Data = _dataRef(pd),
                        List = ups,
                        VersionAfter = _versionRef(ups),
                    });
                }
            }
            return __exception;
        }
    }
}
