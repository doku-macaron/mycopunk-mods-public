using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;
using Cil = Mono.Cecil.Cil;

namespace UpgradeLagFix
{
    /// <summary>
    /// Skips PlayerData.GearData.Cleanup() when it provably cannot remove anything.
    ///
    /// Cleanup removes equipped entries whose upgradeID no longer resolves through
    /// GetUpgradeInstanceFromID. EquippedUpgradeEnumerator.MoveNext calls it on every step,
    /// which makes one walk O(n^2) lookups. A Cleanup that removed nothing stays a no-op as long as
    ///   (a) the GearData's equipped lists are the same objects with the same List._version, and
    ///   (b) nothing has removed an UpgradeInstance / UpgradeInfo from the player's data, or
    ///       replaced PlayerData, its data dictionary, an Instances list, or an instance ID.
    /// (a) is checked directly. For (b), every instruction in Assembly-CSharp that could do such a
    /// removal or store is found at startup with Mono.Cecil, and a call that bumps a generation
    /// counter is inserted right before it. A remembered no-op is only reused while the counter is
    /// unchanged. Additions never turn a resolvable ID into an unresolvable one, so they are ignored.
    ///
    /// Not instrumented, because the IL shows the receiver is never part of the player's data:
    ///   - Clear() on PlayerData.infoBuffer (a static scratch list),
    ///   - Clear() on the result of get_ActiveUpgrades / get_ActiveSkins (per-gear runtime lists).
    /// If an instrumented method cannot be patched, the feature turns itself off.
    /// </summary>
    internal static class CleanupMemo
    {
        private sealed class Memo
        {
            public int Gen;
            public PlayerData Pd;
            public object Data;
            public List<PlayerData.UpgradeEquipData> Ups;
            public int UpsVer;
            public List<PlayerData.UpgradeEquipData> Skins;
            public int SkinsVer;
        }

        public struct State
        {
            public bool Track;
            public bool ExpectNoop;
            public int Gen;
            public List<PlayerData.UpgradeEquipData> Ups;
            public int UpsVer;
            public List<PlayerData.UpgradeEquipData> Skins;
            public int SkinsVer;
        }

        // Matched by name on any collection type (List, Dictionary, HashSet, ...). An indexer setter has no
        // C# name, so set_Item stays a string.
        private static readonly HashSet<string> RemovingMethods = new HashSet<string>
        {
            nameof(List<object>.Remove), nameof(List<object>.RemoveAt), nameof(List<object>.RemoveAll),
            nameof(List<object>.RemoveRange), nameof(List<object>.Clear), "set_Item", nameof(List<object>.TrimExcess),
        };

        private static string FieldKey(string type, string field) => type + "::" + field;

        private static readonly HashSet<string> WatchedFields = new HashSet<string>
        {
            FieldKey(nameof(PlayerData), nameof(PlayerData.data)), FieldKey(nameof(PlayerData), nameof(PlayerData._instance)),
            FieldKey(nameof(UpgradeInfo), nameof(UpgradeInfo.Instances)), FieldKey(nameof(UpgradeInstance), nameof(UpgradeInstance.id)),
        };

        // Extra state that PrepareUpgrades' check (GetUpgrade() == null || IsDestroyed()) depends on.
        // These only bump the prepare generation (PrepGen), not the cleanup generation.
        private static readonly HashSet<string> PrepFields = new HashSet<string>
        {
            FieldKey(nameof(PlayerData), nameof(PlayerData.collectedGear)), FieldKey(nameof(PlayerData), nameof(PlayerData.upgradeIDs)),
            FieldKey(nameof(PlayerData.GearData), nameof(PlayerData.GearData.gear)),
            FieldKey(nameof(UpgradeInstance), nameof(UpgradeInstance.gearID)), FieldKey(nameof(UpgradeInstance), nameof(UpgradeInstance.upgradeIDv2)),
            FieldKey(nameof(UpgradeInfo), nameof(UpgradeInfo.idV2)),
            // Sort keys of UpgradeEquipData.CompareTo that live outside the list. StackingMemo relies
            // on the list staying in sorted order while its _version is unchanged.
            FieldKey(nameof(UpgradeInstance), nameof(UpgradeInstance.timeEquipped)), FieldKey(nameof(Upgrade), nameof(Upgrade._name)),
            // The backing field of the auto-property Upgrade.Priority is compiler-generated, so C# cannot name it.
            FieldKey(nameof(Upgrade), "<Priority>k__BackingField"),
        };

        // GetUpgradeInfo returns the first matching UpgradeInfo, so reordering a List<UpgradeInfo> matters too.
        private static readonly HashSet<string> OrderMethods = new HashSet<string>
        {
            nameof(List<object>.Sort), nameof(List<object>.Reverse), nameof(List<object>.Insert), nameof(List<object>.InsertRange),
        };

        // Cecil full names (nested types use '/').
        private static readonly HashSet<string> CleanupTypeNames = new HashSet<string> { nameof(UpgradeInstance), nameof(UpgradeInfo) };
        private static readonly HashSet<string> PrepTypeNames = new HashSet<string> { nameof(PlayerData) + "/" + nameof(PlayerData.GearData), nameof(Upgrade) };
        private static readonly HashSet<string> InfoTypeNames = new HashSet<string> { nameof(UpgradeInfo) };

        private const int KindNone = 0, KindCleanup = 1, KindPrepare = 2;

        // The getters of IGear.ActiveUpgrades / ActiveSkins and their implementations, as they appear in IL.
        private static readonly HashSet<string> ScratchListGetters = new HashSet<string>
        {
            "get_" + nameof(IGear.ActiveUpgrades), "get_" + nameof(IGear.ActiveSkins),
        };

        private static readonly ConditionalWeakTable<PlayerData.GearData, Memo> Memos = new ConditionalWeakTable<PlayerData.GearData, Memo>();

        // List<T>._version is a private field of mscorlib (not publicized), so it is read through a field ref.
        // Set in Setup, so a missing field only disables this feature.
        private static AccessTools.FieldRef<List<PlayerData.UpgradeEquipData>, int> _versionRef;

        private static readonly MethodInfo BumpMethod = AccessTools.Method(typeof(CleanupMemo), nameof(Bump));
        private static readonly MethodInfo BumpPrepMethod = AccessTools.Method(typeof(CleanupMemo), nameof(BumpPrep));

        private static int _gen;
        private static int _prepGen;

        /// <summary>Changes whenever anything PrepareUpgrades' destroyed-check depends on may have changed.</summary>
        internal static int PrepGen => _prepGen;
        /// <summary>False when some prepare-only site could not be instrumented.</summary>
        internal static bool PrepAvailable { get; private set; }
        private static int _mismatchLogs;

        // Per-site bump counters for diagnostics.
        private static readonly List<string> SiteNames = new List<string>();
        private static long[] _siteCounts = new long[64];

        public static bool Available { get; private set; }
        public static int InstrumentedMethods { get; private set; }
        public static int InstrumentedSites { get; private set; }

        public static void Setup(Harmony harmony)
        {
            var log = Plugin.Log;
            try
            {
                _versionRef = AccessTools.FieldRefAccess<List<PlayerData.UpgradeEquipData>, int>("_version");

                var sw = Stopwatch.StartNew();
                var mutators = FindMutators(out var unpatchable, out var unpatchablePrep);
                log.LogInfo("Cleanup memo: found " + mutators.Count + " methods with watched instructions in " + sw.ElapsedMilliseconds + " ms");
                if (unpatchable.Count > 0)
                {
                    log.LogWarning("Cleanup/prepare memo disabled: cannot patch " + string.Join(", ", unpatchable));
                    return;
                }
                PrepAvailable = unpatchablePrep.Count == 0;
                if (!PrepAvailable)
                    log.LogWarning("Prepare memo disabled: cannot patch " + string.Join(", ", unpatchablePrep));

                InstrumentPatch.Targets = mutators;
                try
                {
                    harmony.CreateClassProcessor(typeof(InstrumentPatch)).Patch();
                    InstrumentedMethods = mutators.Count;
                }
                catch (Exception e)
                {
                    // The exception message names the method that failed.
                    log.LogWarning("Cleanup/prepare memo disabled: failed to patch: " + e.Message);
                    _gen++;
                    _prepGen++;
                    PrepAvailable = false;
                    return;
                }

                harmony.CreateClassProcessor(typeof(CleanupPatch)).Patch();

                Available = true;
                log.LogInfo("Cleanup memo ready: instrumented " + InstrumentedSites + " instructions in " + InstrumentedMethods + " methods");
            }
            catch (Exception e)
            {
                Available = false;
                log.LogError("Cleanup memo setup failed, feature disabled: " + e);
            }
        }

        // ---------------- Static scan (Cecil) ----------------

        private static List<MethodBase> FindMutators(out List<string> unpatchable, out List<string> unpatchablePrep)
        {
            unpatchable = new List<string>();
            unpatchablePrep = new List<string>();
            var result = new List<MethodBase>();
            var module = typeof(PlayerData).Module;
            using (var asm = AssemblyDefinition.ReadAssembly(typeof(PlayerData).Assembly.Location))
            {
                foreach (var type in asm.MainModule.GetTypes())
                {
                    foreach (var md in type.Methods)
                    {
                        if (!md.HasBody) continue;
                        // Static constructors have already run before any memo can exist (every memo
                        // is taken after PlayerData is in use) and never run again.
                        if (md.IsConstructor && md.IsStatic) continue;
                        int kinds = 0;
                        foreach (var ins in md.Body.Instructions) kinds |= ClassifyCecil(ins);
                        if (kinds == 0) continue;
                        string key = md.DeclaringType.FullName + "::" + md.Name;
                        if (md.DeclaringType.IsInterface || md.HasGenericParameters || md.DeclaringType.HasGenericParameters)
                        {
                            if ((kinds & KindCleanup) != 0) unpatchable.Add(key);
                            else unpatchablePrep.Add(key);
                            continue;
                        }
                        MethodBase runtime;
                        try { runtime = module.ResolveMethod(md.MetadataToken.ToInt32()); }
                        catch { runtime = null; }
                        if (runtime == null)
                        {
                            if ((kinds & KindCleanup) != 0) unpatchable.Add(key);
                            else unpatchablePrep.Add(key);
                            continue;
                        }
                        result.Add(runtime);
                    }
                }
            }
            return result;
        }

        private static int ClassifyCecil(Cil.Instruction ins)
        {
            var op = ins.OpCode.Code;
            if ((op == Cil.Code.Call || op == Cil.Code.Callvirt) && ins.Operand is MethodReference mr)
            {
                var dt = mr.DeclaringType;
                if (RemovingMethods.Contains(mr.Name))
                {
                    if (MentionsCecil(dt, CleanupTypeNames))
                        return IsScratchReceiver(mr.Name, ins.Previous) ? KindNone : KindCleanup;
                    if (MentionsCecil(dt, PrepTypeNames)) return KindPrepare;
                }
                if (OrderMethods.Contains(mr.Name) && MentionsCecil(dt, InfoTypeNames)) return KindPrepare;
                return KindNone;
            }
            if ((op == Cil.Code.Stfld || op == Cil.Code.Stsfld) && ins.Operand is FieldReference fr)
            {
                string key = fr.DeclaringType.Name + "::" + fr.Name;
                if (WatchedFields.Contains(key)) return KindCleanup;
                if (PrepFields.Contains(key)) return KindPrepare;
            }
            return KindNone;
        }

        private static bool MentionsCecil(TypeReference t, HashSet<string> names)
        {
            if (names.Contains(t.FullName)) return true;
            if (t is GenericInstanceType g)
                foreach (var a in g.GenericArguments)
                    if (MentionsCecil(a, names)) return true;
            return false;
        }

        private static bool IsScratchReceiver(string method, Cil.Instruction prev)
        {
            // Only parameterless Clear() directly on a known scratch list; the receiver is the
            // instruction right before the call.
            if (method != nameof(List<object>.Clear) || prev == null) return false;
            var op = prev.OpCode.Code;
            if ((op == Cil.Code.Call || op == Cil.Code.Callvirt) && prev.Operand is MethodReference g)
                return ScratchListGetters.Contains(g.Name) && g.Parameters.Count == 0;
            if (op == Cil.Code.Ldsfld && prev.Operand is FieldReference f)
                return f.Name == nameof(PlayerData.infoBuffer) && f.DeclaringType.Name == nameof(PlayerData);
            return false;
        }

        // ---------------- Instrumentation (runtime IL) ----------------

        private static int ClassifyRuntime(CodeInstruction ins, CodeInstruction prev)
        {
            if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodInfo mi)
            {
                var dt = mi.DeclaringType;
                if (dt == null || !dt.IsGenericType) return KindNone;
                if (RemovingMethods.Contains(mi.Name))
                {
                    if (Mentions(dt, typeof(UpgradeInstance), typeof(UpgradeInfo)))
                        return IsScratchReceiverRuntime(mi.Name, prev) ? KindNone : KindCleanup;
                    if (Mentions(dt, typeof(PlayerData.GearData), typeof(Upgrade))) return KindPrepare;
                }
                if (OrderMethods.Contains(mi.Name) && Mentions(dt, typeof(UpgradeInfo))) return KindPrepare;
                return KindNone;
            }
            if ((ins.opcode == OpCodes.Stfld || ins.opcode == OpCodes.Stsfld) && ins.operand is FieldInfo fi && fi.DeclaringType != null)
            {
                string key = fi.DeclaringType.Name + "::" + fi.Name;
                if (WatchedFields.Contains(key)) return KindCleanup;
                if (PrepFields.Contains(key)) return KindPrepare;
            }
            return KindNone;
        }

        private static bool IsScratchReceiverRuntime(string method, CodeInstruction prev)
        {
            if (method != nameof(List<object>.Clear) || prev == null) return false;
            if ((prev.opcode == OpCodes.Call || prev.opcode == OpCodes.Callvirt) && prev.operand is MethodInfo g
                && ScratchListGetters.Contains(g.Name) && g.GetParameters().Length == 0)
                return true;
            return prev.opcode == OpCodes.Ldsfld && prev.operand is FieldInfo f
                && f.Name == nameof(PlayerData.infoBuffer) && f.DeclaringType == typeof(PlayerData);
        }

        private static bool Mentions(Type t, params Type[] targets)
        {
            foreach (var x in targets)
                if (t == x) return true;
            if (!t.IsGenericType) return false;
            foreach (var a in t.GetGenericArguments())
                if (Mentions(a, targets)) return true;
            return false;
        }

        /// <summary>Every method found by the Cecil scan: a bump call goes right before each watched instruction.</summary>
        [HarmonyPatch]
        private static class InstrumentPatch
        {
            internal static List<MethodBase> Targets;

            [HarmonyTargetMethods]
            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
                Instrument(instructions, original);
        }

        private static IEnumerable<CodeInstruction> Instrument(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = instructions.ToList();
            var output = new List<CodeInstruction>(list.Count + 8);
            string where = original?.DeclaringType?.Name + "." + original?.Name;
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                int kind = ClassifyRuntime(ins, i > 0 ? list[i - 1] : null);
                if (kind == KindPrepare && IsInfoListSort(ins))
                {
                    // List<UpgradeInfo>.Sort(): bump only if the order actually changed.
                    // Stack before: (list). We push the site id and call SortInfosTracked(list, site).
                    int site = RegisterSite(where + " @" + i + " Sort, changed order (prepare)");
                    var load = new CodeInstruction(OpCodes.Ldc_I4, site);
                    ins.MoveLabelsTo(load);
                    ins.MoveBlocksTo(load);
                    output.Add(load);
                    output.Add(new CodeInstruction(OpCodes.Call, SortInfosTrackedMethod));
                    InstrumentedSites++;
                    continue;
                }
                if (kind != KindNone)
                {
                    int site = RegisterSite(where + " @" + i + " " + Describe(ins) + (kind == KindPrepare ? " (prepare)" : ""));
                    // Net stack effect is zero, so this can sit right before the watched instruction.
                    var load = new CodeInstruction(OpCodes.Ldc_I4, site);
                    ins.MoveLabelsTo(load);
                    ins.MoveBlocksTo(load);
                    output.Add(load);
                    output.Add(new CodeInstruction(OpCodes.Call, kind == KindPrepare ? BumpPrepMethod : BumpMethod));
                    InstrumentedSites++;
                }
                output.Add(ins);
            }
            return output;
        }

        private static readonly MethodInfo InfoListSort = AccessTools.Method(typeof(List<UpgradeInfo>), nameof(List<UpgradeInfo>.Sort), Type.EmptyTypes);
        private static readonly MethodInfo SortInfosTrackedMethod = AccessTools.Method(typeof(CleanupMemo), nameof(SortInfosTracked));
        private static UpgradeInfo[] _sortBuffer = new UpgradeInfo[256];

        private static bool IsInfoListSort(CodeInstruction ins)
            => (ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo mi && mi == InfoListSort;

        /// <summary>
        /// Replacement for List&lt;UpgradeInfo&gt;.Sort() at instrumented sites. Sorts exactly as the
        /// original, then bumps the prepare generation only if the element order changed (or the
        /// sort threw part-way). Reordering is the only way a Sort can change GetUpgradeInfo's answer.
        /// </summary>
        public static void SortInfosTracked(List<UpgradeInfo> list, int site)
        {
            int n = list.Count;
            if (_sortBuffer.Length < n) _sortBuffer = new UpgradeInfo[Math.Max(n, _sortBuffer.Length * 2)];
            var before = _sortBuffer;
            list.CopyTo(before, 0);
            bool completed = false;
            try
            {
                list.Sort();
                completed = true;
            }
            finally
            {
                bool changed = !completed || list.Count != n;
                for (int i = 0; !changed && i < n; i++)
                    if (!ReferenceEquals(before[i], list[i])) changed = true;
                if (!changed) Stats.InfoSortUnchanged++;
                else if (completed && list.Count == n && ReorderIsUnobservable(before, list, n)) Stats.InfoSortHarmless++;
                else BumpPrep(site);
                Array.Clear(before, 0, n);
            }
        }

        private static readonly Dictionary<Upgrade, UpgradeInfo> _firstInfo = new Dictionary<Upgrade, UpgradeInfo>(RefEq<Upgrade>.Instance);
        private static readonly HashSet<int> _seenIds = new HashSet<int>();

        private sealed class RefEq<T> : IEqualityComparer<T> where T : class
        {
            public static readonly RefEq<T> Instance = new RefEq<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
        }

        /// <summary>
        /// UpgradeInfo.CompareTo only compares the Upgrade, so infos of equal Upgrades come out of the
        /// (unstable) Sort in arbitrary order. The order of one data list is read in two ways only:
        ///   - GetUpgradeInfo(gear, upgrade) returns the first info whose Upgrade is that object;
        ///   - GetUpgradeInstanceFromID returns the first instance with that ID, scanning the infos in
        ///     order, and throws on a null instance.
        /// The reorder is unobservable when the first info of every Upgrade is the same object as
        /// before, and no instance ID occurs twice in the list (so no first match can move), and no
        /// instance is null. Anything unusual counts as observable.
        /// </summary>
        private static bool ReorderIsUnobservable(UpgradeInfo[] before, List<UpgradeInfo> after, int n)
        {
            try
            {
                _firstInfo.Clear();
                for (int i = 0; i < n; i++)
                {
                    var info = before[i];
                    if (info == null) return false;
                    var up = info.Upgrade;
                    if (ReferenceEquals(up, null)) return false;
                    if (!_firstInfo.ContainsKey(up)) _firstInfo.Add(up, info);
                }
                int firsts = 0;
                for (int i = 0; i < n; i++)
                {
                    var info = after[i];
                    var up = info.Upgrade;
                    if (ReferenceEquals(up, null) || !_firstInfo.TryGetValue(up, out var first)) return false;
                    if (first == null) continue; // already matched this Upgrade's first info
                    if (!ReferenceEquals(first, info)) return false;
                    _firstInfo[up] = null;
                    firsts++;
                }
                if (firsts != _firstInfo.Count) return false;

                _seenIds.Clear();
                for (int i = 0; i < n; i++)
                {
                    var insts = after[i].Instances;
                    if (insts == null) continue; // GetUpgradeInstanceFromID skips these
                    for (int j = 0; j < insts.Count; j++)
                    {
                        var inst = insts[j];
                        if (inst == null) return false;
                        if (!_seenIds.Add(inst.InstanceID)) return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _firstInfo.Clear();
                _seenIds.Clear();
            }
        }

        private static string Describe(CodeInstruction ins)
        {
            if (ins.operand is MethodInfo mi) return mi.Name;
            if (ins.operand is FieldInfo fi) return "store " + fi.Name;
            return ins.opcode.Name;
        }

        private static int RegisterSite(string name)
        {
            SiteNames.Add(name);
            if (SiteNames.Count > _siteCounts.Length) Array.Resize(ref _siteCounts, _siteCounts.Length * 2);
            return SiteNames.Count - 1;
        }

        /// <summary>Something Cleanup depends on may change: bumps both generations.</summary>
        public static void Bump(int site)
        {
            _gen++;
            _prepGen++;
            Stats.GenBumps++;
            if ((uint)site < (uint)_siteCounts.Length) _siteCounts[site]++;
        }

        /// <summary>Something only PrepareUpgrades' destroyed-check depends on may change.</summary>
        public static void BumpPrep(int site)
        {
            _prepGen++;
            Stats.PrepGenBumps++;
            if ((uint)site < (uint)_siteCounts.Length) _siteCounts[site]++;
        }

        public static string TopSites(int max)
        {
            var top = Enumerable.Range(0, SiteNames.Count)
                .Where(i => _siteCounts[i] > 0)
                .OrderByDescending(i => _siteCounts[i])
                .Take(max)
                .Select(i => _siteCounts[i] + "x " + SiteNames[i]);
            return string.Join("; ", top);
        }

        public static void ResetSiteCounts() => Array.Clear(_siteCounts, 0, _siteCounts.Length);

        // ---------------- Cleanup ----------------

        [HarmonyPatch(typeof(PlayerData.GearData), nameof(PlayerData.GearData.Cleanup))]
        private static class CleanupPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerData.GearData __instance, out State __state)
            {
                __state = default;
                if (!Plugin.CleanupMemoActive) return true;

                var ups = __instance.equippedUpgrades;
                var skins = __instance.equippedSkins;
                var pd = PlayerData.Instance;
                if (pd == null) return true;

                if (Memos.TryGetValue(__instance, out var m)
                    && m.Gen == _gen
                    && ReferenceEquals(m.Pd, pd)
                    && ReferenceEquals(m.Data, pd.data)
                    && ReferenceEquals(m.Ups, ups) && (ups == null || _versionRef(ups) == m.UpsVer)
                    && ReferenceEquals(m.Skins, skins) && (skins == null || _versionRef(skins) == m.SkinsVer))
                {
                    if (!Plugin.VerifyActive)
                    {
                        Stats.CleanupSkipped++;
                        return false;
                    }
                    __state.ExpectNoop = true;
                }

                __state.Track = true;
                __state.Gen = _gen;
                __state.Ups = ups;
                __state.UpsVer = ups == null ? 0 : _versionRef(ups);
                __state.Skins = skins;
                __state.SkinsVer = skins == null ? 0 : _versionRef(skins);
                return true;
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, PlayerData.GearData __instance, ref State __state)
            {
                if (!__state.Track) return __exception;
                Stats.CleanupRun++;

                var ups = __instance.equippedUpgrades;
                var skins = __instance.equippedSkins;
                bool noop = __exception == null
                    && ReferenceEquals(ups, __state.Ups) && (ups == null || _versionRef(ups) == __state.UpsVer)
                    && ReferenceEquals(skins, __state.Skins) && (skins == null || _versionRef(skins) == __state.SkinsVer);

                if (__state.ExpectNoop)
                {
                    Stats.CleanupVerifyChecks++;
                    if (!noop)
                    {
                        Stats.CleanupVerifyMismatches++;
                        if (_mismatchLogs++ < 20)
                            Plugin.Log.LogError("CLEANUP VERIFY MISMATCH: memo said no-op but Cleanup changed the equipped list");
                    }
                }

                Memos.Remove(__instance);
                if (noop && __state.Gen == _gen)
                {
                    var pd = PlayerData.Instance;
                    if (pd != null)
                    {
                        Memos.Add(__instance, new Memo
                        {
                            Gen = _gen,
                            Pd = pd,
                            Data = pd.data,
                            Ups = ups,
                            UpsVer = __state.UpsVer,
                            Skins = skins,
                            SkinsVer = __state.SkinsVer,
                        });
                    }
                }
                return __exception;
            }
        }

        /// <summary>Changes whenever a Cleanup no-op may stop being one.</summary>
        internal static int Gen => _gen;

        /// <summary>True while a remembered no-op Cleanup is still valid for this GearData.</summary>
        internal static bool IsClean(PlayerData.GearData gearData, PlayerData pd)
        {
            if (!Plugin.CleanupMemoActive || pd == null) return false;
            var ups = gearData.equippedUpgrades;
            var skins = gearData.equippedSkins;
            return Memos.TryGetValue(gearData, out var m)
                && m.Gen == _gen
                && ReferenceEquals(m.Pd, pd)
                && ReferenceEquals(m.Data, pd.data)
                && ReferenceEquals(m.Ups, ups) && (ups == null || _versionRef(ups) == m.UpsVer)
                && ReferenceEquals(m.Skins, skins) && (skins == null || _versionRef(skins) == m.SkinsVer);
        }

        /// <summary>
        /// Call right after IsClean: the equipped list was re-sorted in place without change. A real
        /// walk would run Cleanup once on the new version, find nothing and remember that version.
        /// </summary>
        internal static void AdvanceUpsVersion(PlayerData.GearData gearData, int version)
        {
            if (Memos.TryGetValue(gearData, out var m)) m.UpsVer = version;
        }

        public static void Clear()
        {
            _gen++;
            _prepGen++;
        }
    }
}
