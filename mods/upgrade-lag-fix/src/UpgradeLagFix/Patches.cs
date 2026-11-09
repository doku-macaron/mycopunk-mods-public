using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;

namespace UpgradeLagFix
{
    internal static class Patches
    {
        private static ManualLogSource Log => Plugin.Log;

        // Set at startup from Profiling.Detailed. Per-call hooks are only installed when true.
        internal static bool Detailed;

        public static void ApplyAll(Harmony harmony, bool detailed)
        {
            Detailed = detailed;

            Apply(harmony, typeof(LookupPatch));

            // IGear.ApplyUpgrades is a default interface method, which Harmony on Mono cannot
            // patch ("Owner can't be an array or an interface"). Instead, redirect its call
            // sites to TimedApplyUpgrades, which times the call and then makes the same call.
            // One call site per Patch call, as before the attribute rewrite: a caller that fails (for example after
            // a game update) must not keep the other call sites from being redirected. A class processor stops at
            // the first failing target.
            var redirect = new HarmonyMethod(AccessTools.Method(typeof(RedirectApplyUpgradesPatch), nameof(RedirectApplyUpgradesPatch.Transpiler)));
            foreach (var caller in RedirectApplyUpgradesPatch.TargetMethods())
            {
                try
                {
                    harmony.Patch(caller, transpiler: redirect);
                    Log.LogInfo("Patched " + Describe(caller));
                }
                catch (Exception e)
                {
                    Log.LogError("Failed to patch " + Describe(caller) + ": " + e);
                }
            }
            Apply(harmony, typeof(ReapplyPatch));

            if (!detailed)
            {
                Log.LogInfo("Detailed profiling off: per-call hooks (GetStackingModifier, GetAppliedCount, MoveNext, Cleanup counter, lookup timing) not installed");
                return;
            }

            Apply(harmony, typeof(StackingTimingPatch));
            Apply(harmony, typeof(AppliedTimingPatch));
            Apply(harmony, typeof(MoveNextCountPatch));
            Apply(harmony, typeof(CleanupCountPatch));
        }

        /// <summary>Applies one patch class; a failure is logged and does not stop the others.</summary>
        private static void Apply(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                foreach (var original in harmony.GetPatchedMethods())
                    if (IsPatchedBy(original, patchClass))
                        Log.LogInfo("Patched " + Describe(original));
            }
            catch (Exception e)
            {
                Log.LogError("Failed to patch " + patchClass.Name + ": " + e);
            }
        }

        private static bool IsPatchedBy(MethodBase original, Type patchClass)
        {
            var info = Harmony.GetPatchInfo(original);
            if (info == null) return false;
            return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                .Any(p => p.PatchMethod?.DeclaringType == patchClass);
        }

        private static string Describe(MethodBase target) =>
            target.DeclaringType?.Name + "." + target.Name + "(" + target.GetParameters().Length + ")";

        // ---------------- GetUpgradeInstanceFromID ----------------

        public struct LookupState
        {
            public long Start;
            public bool RanOriginal;
            public bool FeedCache;
            public bool Verify;
            public bool CachedHit;
            public UpgradeInstance Cached;
        }

        private static int _mismatchLogs;

        [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.GetUpgradeInstanceFromID), new[] { typeof(int) })]
        private static class LookupPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(int id, ref UpgradeInstance __result, out LookupState __state)
            {
                __state = default;
                if (Detailed) __state.Start = Stopwatch.GetTimestamp();
                Stats.Lookups++;

                if (!Plugin.CacheActive)
                {
                    __state.RanOriginal = true;
                    Stats.OriginalCalls++;
                    return true;
                }

                bool hit = false;
                UpgradeInstance cached = null;
                try
                {
                    hit = InstanceCache.TryGet(id, out cached);
                }
                catch (Exception e)
                {
                    Plugin.OnCacheFault(e);
                    __state.RanOriginal = true;
                    Stats.OriginalCalls++;
                    return true;
                }

                if (Plugin.VerifyActive)
                {
                    // Always return the original result; only compare.
                    __state.Verify = true;
                    __state.CachedHit = hit;
                    __state.Cached = cached;
                    __state.RanOriginal = true;
                    __state.FeedCache = !hit;
                    Stats.OriginalCalls++;
                    return true;
                }

                if (hit)
                {
                    __result = cached;
                    Stats.CacheHits++;
                    return false;
                }

                __state.RanOriginal = true;
                __state.FeedCache = true;
                Stats.OriginalCalls++;
                return true;
            }

            [HarmonyPostfix]
            private static void Postfix(int id, UpgradeInstance __result, ref LookupState __state)
            {
                if (__state.RanOriginal)
                {
                    if (__result == null) Stats.OriginalNull++;

                    if (__state.Verify)
                    {
                        Stats.VerifyChecks++;
                        if (__state.CachedHit && !ReferenceEquals(__state.Cached, __result))
                        {
                            Stats.VerifyMismatches++;
                            if (_mismatchLogs++ < 20)
                                Log.LogError("VERIFY MISMATCH id=" + id + " cached=" + Describe(__state.Cached) + " original=" + Describe(__result));
                        }
                    }

                    if (__state.FeedCache && Plugin.CacheActive)
                    {
                        try { InstanceCache.OnOriginalResult(id, __result); }
                        catch (Exception e) { Plugin.OnCacheFault(e); }
                    }
                }
                if (Detailed) Stats.LookupTicks += Stopwatch.GetTimestamp() - __state.Start;
            }
        }

        private static string Describe(UpgradeInstance inst)
        {
            if (inst == null) return "null";
            try { return "#" + inst.InstanceID + " " + inst.Upgrade?.name; }
            catch { return "#?"; }
        }

        // ---------------- ApplyUpgrades call sites ----------------

        private static readonly MethodInfo ApplyUpgradesMethod = AccessTools.Method(typeof(IGear), nameof(IGear.ApplyUpgrades));
        private static readonly MethodInfo TimedApplyUpgradesMethod = AccessTools.Method(typeof(Patches), nameof(TimedApplyUpgrades));

        /// <summary>
        /// Not applied as a class (see ApplyAll): its target list and transpiler are used one target at a time.
        /// </summary>
        private static class RedirectApplyUpgradesPatch
        {
            // Call sites of IGear.ApplyUpgrades in Assembly-CSharp (game v2.0.4, from the IL dump).
            internal static IEnumerable<MethodBase> TargetMethods()
            {
                var callers = new[]
                {
                    (typeof(GearUtil), nameof(GearUtil.ReapplyUpgrades)),
                    (typeof(Gun), nameof(Gun.Setup)),
                    (typeof(MycoGear), nameof(MycoGear.Setup)),
                    (typeof(ScrapperGear), nameof(ScrapperGear.Setup)),
                    (typeof(Throwable), nameof(Throwable.Setup)),
                    (typeof(Wingsuit), nameof(Wingsuit.Setup)),
                    (typeof(EquipGearInteractable), nameof(EquipGearInteractable.EquipGear_ClientRpc)),
                };
                foreach (var (type, method) in callers)
                    foreach (var m in AccessTools.GetDeclaredMethods(type))
                        if (m.Name == method && !m.IsAbstract) yield return m;

                var close = AccessTools.Method(typeof(Menu), nameof(Menu.CloseCoroutine));
                var moveNext = close == null ? null : AccessTools.EnumeratorMoveNext(close);
                if (moveNext != null) yield return moveNext;
                else Log.LogWarning("Menu.CloseCoroutine MoveNext not found");
            }

            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            {
                int replaced = 0;
                foreach (var ins in instructions)
                {
                    if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo mi && mi == ApplyUpgradesMethod)
                    {
                        // Same stack shape: (IGear) -> void. Keep labels and exception blocks.
                        ins.opcode = OpCodes.Call;
                        ins.operand = TimedApplyUpgradesMethod;
                        replaced++;
                    }
                    yield return ins;
                }
                // Harmony reruns all transpilers whenever another patch is added to the same method.
                string where = original?.DeclaringType?.Name + "." + original?.Name;
                if (replaced == 0) Log.LogWarning("No IGear.ApplyUpgrades call found in " + where);
                else Log.LogInfo("  redirected " + replaced + " ApplyUpgrades call(s) in " + where);
            }
        }

        public static void TimedApplyUpgrades(IGear gear)
        {
            var s = BeginOp(ref _applyDepth);
            try
            {
                gear.ApplyUpgrades();
            }
            finally
            {
                long t = EndOp(ref _applyDepth, s);
                if (t >= 0)
                {
                    Stats.ApplyCalls++;
                    Stats.ApplyTicks += t;
                    if (t > Stats.ApplyMaxTicks) Stats.ApplyMaxTicks = t;
                    if (_reapplyDepth == 0) LogOp("ApplyUpgrades", GearName(gear), t, s);
                }
            }
        }

        // ---------------- Top-level operations ----------------

        public struct OpState
        {
            public long Start;
            public long Lookups;
            public long Stacking;
            public long MoveNext;
            public long ApplyTicks;
            public long Bumps;
            public bool Outermost;
        }

        private static int _applyDepth;
        private static int _reapplyDepth;

        private static OpState BeginOp(ref int depth)
        {
            var s = new OpState { Outermost = depth++ == 0 };
            if (s.Outermost)
            {
                s.Lookups = Stats.Lookups;
                s.Stacking = Stats.StackingCalls;
                s.MoveNext = Stats.EnumMoveNext;
                s.ApplyTicks = Stats.ApplyTicks;
                s.Bumps = Stats.GenBumps;
                s.Start = Stopwatch.GetTimestamp();
            }
            return s;
        }

        private static long EndOp(ref int depth, in OpState s)
        {
            depth--;
            if (!s.Outermost) return -1;
            return Stopwatch.GetTimestamp() - s.Start;
        }

        private static void LogOp(string what, string target, long ticks, in OpState s)
        {
            double ms = ticks * Stats.TicksToMs;
            if (ms < Plugin.LogThresholdMs) return;
            string inner = what == "ReapplyUpgrades"
                ? string.Format(" (ApplyUpgrades inside {0:F2} ms)", (Stats.ApplyTicks - s.ApplyTicks) * Stats.TicksToMs)
                : "";
            Log.LogInfo(string.Format("[perf] {0}{1}: {2:F2} ms{3} | lookups {4}, stacking {5}, moveNext {6}, gen bumps {7} | {8}",
                what, target == null ? "" : "[" + target + "]", ms, inner,
                Stats.Lookups - s.Lookups, Stats.StackingCalls - s.Stacking, Stats.EnumMoveNext - s.MoveNext,
                Stats.GenBumps - s.Bumps, Plugin.ModeString));
        }

        [HarmonyPatch(typeof(GearUtil), nameof(GearUtil.ReapplyUpgrades))]
        private static class ReapplyPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out OpState __state) => __state = BeginOp(ref _reapplyDepth);

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, OpState __state)
            {
                long t = EndOp(ref _reapplyDepth, __state);
                if (t >= 0)
                {
                    Stats.ReapplyCalls++;
                    Stats.ReapplyTicks += t;
                    if (t > Stats.ReapplyMaxTicks) Stats.ReapplyMaxTicks = t;
                    LogOp("ReapplyUpgrades", null, t, __state);
                }
                return __exception;
            }
        }

        internal static string GearName(IUpgradable gear)
        {
            try { return gear?.Info?.Name ?? "?"; }
            catch { return "?"; }
        }

        // ---------------- Hot callers ----------------

        private static int _stackDepth;
        private static int _appliedDepth;

        [HarmonyPatch]
        private static class StackingTimingPatch
        {
            // Both overloads: the 3-argument one calls the 5-argument one, so only the outermost call is timed.
            [HarmonyTargetMethods]
            private static IEnumerable<MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(UpgradeProperty))
                    .Where(m => m.Name == nameof(UpgradeProperty.GetStackingModifier))
                    .Cast<MethodBase>();

            [HarmonyPrefix]
            private static void Prefix(out long __state)
                => __state = _stackDepth++ == 0 ? Stopwatch.GetTimestamp() : 0;

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, long __state)
            {
                if (--_stackDepth == 0)
                {
                    Stats.StackingCalls++;
                    Stats.StackingTicks += Stopwatch.GetTimestamp() - __state;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(UpgradeProperty), nameof(UpgradeProperty.GetAppliedCount))]
        private static class AppliedTimingPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state)
                => __state = _appliedDepth++ == 0 ? Stopwatch.GetTimestamp() : 0;

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, long __state)
            {
                if (--_appliedDepth == 0)
                {
                    Stats.AppliedCountCalls++;
                    Stats.AppliedCountTicks += Stopwatch.GetTimestamp() - __state;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(PlayerData.EquippedUpgradeEnumerator), nameof(PlayerData.EquippedUpgradeEnumerator.MoveNext))]
        private static class MoveNextCountPatch
        {
            [HarmonyPrefix]
            private static void Prefix() => Stats.EnumMoveNext++;
        }

        [HarmonyPatch(typeof(PlayerData.GearData), nameof(PlayerData.GearData.Cleanup))]
        private static class CleanupCountPatch
        {
            [HarmonyPrefix]
            private static void Prefix() => Stats.GearDataCleanup++;
        }
    }
}
