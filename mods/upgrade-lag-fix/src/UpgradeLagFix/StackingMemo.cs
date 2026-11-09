using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UpgradeLagFix
{
    /// <summary>
    /// Answers UpgradeProperty.GetStackingModifier(gear, instance, coefficient, mustBeEquipped, out prevCount)
    /// and UpgradeProperty.GetAppliedCount(gear, instance, mustBeEquipped) from a per-gear rank table.
    ///
    /// Both originals resolve the target gear, walk its equipped upgrades in apply order
    /// (EquippedUpgradeEnumerator with sort = true), count entries whose Upgrade equals
    /// instance.Upgrade and stop at the instance itself. So the answer is only:
    ///   count = number of same-Upgrade entries before the first occurrence of instance
    ///           (or all of them when instance is not equipped), found = whether it was reached.
    /// Every upgrade asks this once or more while applying, which makes one apply O(n^2).
    ///
    /// The table maps each equipped instance to that rank, plus a total per Upgrade, built in one
    /// pass over the list. It is used only while a real walk would provably be a pure read:
    ///   - PrepareMemo and CleanupMemo both hold a valid no-op memo for this GearData, so the
    ///     destroyed-check and every Cleanup would remove nothing, and
    ///   - the list is the same object with the same List._version as when the table was taken,
    ///     right after a real walk (so it is in sorted order), with the same generations.
    /// The one observable effect of such a walk is that Sort() increments List._version. The fast
    /// path does the same and advances both memos to the new version, which is the state a real
    /// walk leaves behind (Prepare stores the post-sort version, the first Cleanup re-records it).
    ///
    /// Not handled (falls back to the original): null gear/instance, no GearData or list, any
    /// entry that does not resolve to an instance or whose Upgrade is null, any exception.
    /// With Cache.Verify the original always runs and its result is compared with the table.
    /// </summary>
    internal static class StackingMemo
    {
        private sealed class RefEq<T> : IEqualityComparer<T> where T : class
        {
            public static readonly RefEq<T> Instance = new RefEq<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
        }

        private sealed class Table
        {
            // Key: the state the table describes.
            public List<PlayerData.UpgradeEquipData> List;
            public int Version;
            public int Gen;
            public int PrepGen;
            public PlayerData Pd;
            public object Data;
            public bool Built;
            // Do not retry a failed build until the generations change.
            public bool Failed;
            public int FailGen;
            public int FailPrepGen;

            public readonly Dictionary<UpgradeInstance, int> FirstRank = new Dictionary<UpgradeInstance, int>(RefEq<UpgradeInstance>.Instance);
            public readonly Dictionary<Upgrade, int> Total = new Dictionary<Upgrade, int>(RefEq<Upgrade>.Instance);

            public bool Matches(List<PlayerData.UpgradeEquipData> ups, int ver, PlayerData pd, object data) =>
                ReferenceEquals(List, ups) && Version == ver && Gen == CleanupMemo.Gen && PrepGen == CleanupMemo.PrepGen
                && ReferenceEquals(Pd, pd) && ReferenceEquals(Data, data);

            public void SetKey(List<PlayerData.UpgradeEquipData> ups, int ver, int gen, int prepGen, PlayerData pd, object data)
            {
                List = ups;
                Version = ver;
                Gen = gen;
                PrepGen = prepGen;
                Pd = pd;
                Data = data;
                Built = false;
            }

            public void Invalidate()
            {
                List = null;
                Built = false;
            }
        }

        public struct State
        {
            public bool Track;
            public PlayerData.GearData Gd;
            public List<PlayerData.UpgradeEquipData> Ups;
            public int VerBefore;
            public int CountBefore;
            public int Gen;
            public int PrepGen;
            public bool TableUsable;
            public bool HasPrediction;
            public int PredCount;
            public bool PredFound;
        }

        private static readonly ConditionalWeakTable<PlayerData.GearData, Table> Tables = new ConditionalWeakTable<PlayerData.GearData, Table>();

        // List<T>._version is a private field of mscorlib (not publicized), so it is read through a field ref.
        // Set in Setup, so a missing field only disables this feature.
        private static AccessTools.FieldRef<List<PlayerData.UpgradeEquipData>, int> _versionRef;
        private static int _mismatchLogs;

        public static bool Available { get; private set; }

        public static void Setup(Harmony harmony)
        {
            var log = Plugin.Log;
            try
            {
                if (!(CleanupMemo.Available && PrepareMemo.PrepareAvailable))
                {
                    log.LogWarning("Stacking table disabled: needs both the cleanup and the prepare memo");
                    return;
                }
                _versionRef = AccessTools.FieldRefAccess<List<PlayerData.UpgradeEquipData>, int>("_version");
                harmony.CreateClassProcessor(typeof(StackingPatch)).Patch();
                harmony.CreateClassProcessor(typeof(AppliedPatch)).Patch();
                Available = true;
                log.LogInfo("Stacking table ready");
            }
            catch (Exception e)
            {
                Available = false;
                log.LogError("Stacking table setup failed, feature disabled: " + e);
            }
        }

        // ---------------- Patches ----------------

        [HarmonyPatch(typeof(UpgradeProperty), nameof(UpgradeProperty.GetStackingModifier),
            new[] { typeof(IUpgradable), typeof(UpgradeInstance), typeof(float), typeof(bool), typeof(int) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
        private static class StackingPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(IUpgradable gear, UpgradeInstance instance, float coefficient, bool mustBeEquipped,
                ref int prevCount, ref float __result, out State __state)
            {
                if (!Begin(gear, instance, out __state, out var count, out var found)) return true;
                if (Plugin.VerifyActive)
                {
                    __state.HasPrediction = true;
                    __state.PredCount = count;
                    __state.PredFound = found;
                    return true;
                }
                Commit(ref __state);
                if (mustBeEquipped && !found)
                {
                    prevCount = 0;
                    __result = 1f;
                }
                else
                {
                    prevCount = count;
                    __result = (float)count * coefficient + 1f;
                }
                return false;
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, UpgradeInstance instance, float coefficient, bool mustBeEquipped,
                int prevCount, ref float __result, ref State __state)
            {
                if (!__state.Track) return __exception;
                if (__state.HasPrediction && __exception == null)
                {
                    int expCount = mustBeEquipped && !__state.PredFound ? 0 : __state.PredCount;
                    float expResult = mustBeEquipped && !__state.PredFound ? 1f : (float)__state.PredCount * coefficient + 1f;
                    Check(expCount == prevCount && BitConverter.SingleToInt32Bits(expResult) == BitConverter.SingleToInt32Bits(__result),
                        "GetStackingModifier", instance, "table count " + expCount + " / " + expResult.ToString("R") +
                        ", original " + prevCount + " / " + __result.ToString("R"));
                }
                AfterOriginal(ref __state, __exception);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(UpgradeProperty), nameof(UpgradeProperty.GetAppliedCount),
            new[] { typeof(IUpgradable), typeof(UpgradeInstance), typeof(bool) })]
        private static class AppliedPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(IUpgradable gear, UpgradeInstance instance, bool mustBeEquipped, ref int __result, out State __state)
            {
                if (!Begin(gear, instance, out __state, out var count, out var found)) return true;
                if (Plugin.VerifyActive)
                {
                    __state.HasPrediction = true;
                    __state.PredCount = count;
                    __state.PredFound = found;
                    return true;
                }
                Commit(ref __state);
                __result = mustBeEquipped && !found ? 0 : count;
                return false;
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, UpgradeInstance instance, bool mustBeEquipped, int __result, ref State __state)
            {
                if (!__state.Track) return __exception;
                if (__state.HasPrediction && __exception == null)
                {
                    int exp = mustBeEquipped && !__state.PredFound ? 0 : __state.PredCount;
                    Check(exp == __result, "GetAppliedCount", instance, "table " + exp + ", original " + __result);
                }
                AfterOriginal(ref __state, __exception);
                return __exception;
            }
        }

        // ---------------- Core ----------------

        /// <summary>
        /// Same target resolution as the originals: gear.Prefab ?? gear; a MonoBehaviour that is some
        /// character's utility item becomes that character (the last match); Global becomes the local
        /// player's character; the walk then uses target.Prefab ?? target.
        /// </summary>
        private static IUpgradable ResolveWalkTarget(IUpgradable gear)
        {
            IUpgradable t = gear.Prefab ?? gear;
            if (t is MonoBehaviour mb)
            {
                GameObject go = mb.gameObject;
                var chars = Global.Instance.Characters;
                for (int i = 0; i < chars.Length; i++)
                {
                    if ((object)go == (object)chars[i].UtilityItem) t = chars[i];
                }
            }
            else if (ReferenceEquals(t, Global.Instance))
            {
                t = Pigeon.Movement.Player.LocalPlayer.Character;
            }
            return t.Prefab ?? t;
        }

        /// <summary>
        /// Fills __state for tracking. Returns true with the table's answer when the table may be used.
        /// Returning false means "run the original".
        /// </summary>
        private static bool Begin(IUpgradable gear, UpgradeInstance instance, out State st, out int count, out bool found)
        {
            st = default;
            count = 0;
            found = false;
            if (!Plugin.StackTableActive || gear == null || instance == null) return false;
            try
            {
                var gd = PlayerData.GetGearData(ResolveWalkTarget(gear));
                if (gd == null) return false;
                var ups = gd.equippedUpgrades;
                var pd = PlayerData.Instance;
                if (ups == null || pd == null) return false;
                int ver = _versionRef(ups);

                st.Track = true;
                st.Gd = gd;
                st.Ups = ups;
                st.VerBefore = ver;
                st.CountBefore = ups.Count;
                st.Gen = CleanupMemo.Gen;
                st.PrepGen = CleanupMemo.PrepGen;

                if (!IsClean(gd, ups, pd)) return false;
                var data = pd.data;
                var t = Tables.GetValue(gd, _ => new Table());
                if (!t.Matches(ups, ver, pd, data)) return false;
                if (!t.Built && !Build(t, ups)) return false;
                st.TableUsable = true;

                Upgrade up = instance.Upgrade;
                if (up == null) return false;
                if (t.FirstRank.TryGetValue(instance, out count))
                {
                    found = true;
                }
                else
                {
                    t.Total.TryGetValue(up, out count);
                    found = false;
                }
                return true;
            }
            catch
            {
                // Let the original reproduce whatever happens.
                st = default;
                count = 0;
                found = false;
                return false;
            }
        }

        private static bool IsClean(PlayerData.GearData gd, List<PlayerData.UpgradeEquipData> ups, PlayerData pd) =>
            PrepareMemo.IsClean(gd, ups, pd) && CleanupMemo.IsClean(gd, pd);

        private static bool Build(Table t, List<PlayerData.UpgradeEquipData> ups)
        {
            if (t.Failed && t.FailGen == CleanupMemo.Gen && t.FailPrepGen == CleanupMemo.PrepGen) return false;
            t.FirstRank.Clear();
            t.Total.Clear();
            for (int i = 0; i < ups.Count; i++)
            {
                var inst = PlayerData.GetUpgradeInstanceFromID(ups[i].upgradeID);
                Upgrade up = inst?.Upgrade;
                if (inst == null || up == null)
                {
                    t.FirstRank.Clear();
                    t.Total.Clear();
                    t.Failed = true;
                    t.FailGen = CleanupMemo.Gen;
                    t.FailPrepGen = CleanupMemo.PrepGen;
                    Stats.StackBuildFailed++;
                    return false;
                }
                t.Total.TryGetValue(up, out int before);
                if (!t.FirstRank.ContainsKey(inst)) t.FirstRank.Add(inst, before);
                t.Total[up] = before + 1;
            }
            t.Built = true;
            t.Failed = false;
            Stats.StackBuilds++;
            return true;
        }

        /// <summary>Reproduces the state a real (no-op) walk leaves behind: Sort() bumped List._version.</summary>
        private static void Commit(ref State st)
        {
            var ups = st.Ups;
            int ver = ++_versionRef(ups);
            PrepareMemo.AdvanceVersion(st.Gd, ver);
            CleanupMemo.AdvanceUpsVersion(st.Gd, ver);
            if (Tables.TryGetValue(st.Gd, out var t)) t.Version = ver;
            Stats.StackFast++;
            st.Track = false;
        }

        /// <summary>After the original ran: keep the table if the walk only re-sorted, otherwise re-key it.</summary>
        private static void AfterOriginal(ref State st, Exception exception)
        {
            Stats.StackRun++;
            try
            {
                var gd = st.Gd;
                var t = Tables.GetValue(gd, _ => new Table());
                var ups = gd.equippedUpgrades;
                var pd = PlayerData.Instance;
                if (exception != null || ups == null || pd == null || !IsClean(gd, ups, pd))
                {
                    t.Invalidate();
                    return;
                }
                int ver = _versionRef(ups);
                bool onlySorted = st.TableUsable && t.Built
                    && ReferenceEquals(ups, st.Ups) && ver == st.VerBefore + 1 && ups.Count == st.CountBefore
                    && st.Gen == CleanupMemo.Gen && st.PrepGen == CleanupMemo.PrepGen
                    && ReferenceEquals(t.Pd, pd) && ReferenceEquals(t.Data, pd.data);
                if (onlySorted) t.Version = ver;
                else t.SetKey(ups, ver, CleanupMemo.Gen, CleanupMemo.PrepGen, pd, pd.data);
            }
            catch
            {
                if (st.Gd != null && Tables.TryGetValue(st.Gd, out var t)) t.Invalidate();
            }
        }

        private static void Check(bool ok, string what, UpgradeInstance instance, string detail)
        {
            Stats.StackVerifyChecks++;
            if (ok) return;
            Stats.StackVerifyMismatches++;
            if (_mismatchLogs++ < 20)
            {
                string name;
                try { name = instance?.Upgrade?.APIName ?? "?"; } catch { name = "?"; }
                Plugin.Log.LogError("STACKING VERIFY MISMATCH in " + what + " for " + name + ": " + detail);
            }
        }
    }
}
