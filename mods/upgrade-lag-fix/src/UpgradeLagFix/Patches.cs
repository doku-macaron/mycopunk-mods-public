using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var gearData = AccessTools.Inner(typeof(PlayerData), "GearData");

            Patch(harmony, AccessTools.Method(typeof(PlayerData), nameof(PlayerData.GetUpgradeInstanceFromID), new[] { typeof(int) }),
                prefix: nameof(Lookup_Prefix), postfix: nameof(Lookup_Postfix));

            // IGear.ApplyUpgrades is a default interface method, which Harmony on Mono cannot
            // patch ("Owner can't be an array or an interface"). Instead, redirect its call
            // sites to TimedApplyUpgrades, which times the call and then makes the same call.
            foreach (var caller in ApplyUpgradesCallers())
                Patch(harmony, caller, transpiler: nameof(RedirectApplyUpgrades_Transpiler));
            Patch(harmony, AccessTools.Method(typeof(GearUtil), nameof(GearUtil.ReapplyUpgrades)),
                prefix: nameof(Reapply_Prefix), finalizer: nameof(Reapply_Finalizer));

            if (!detailed)
            {
                Log.LogInfo("Detailed profiling off: per-call hooks (GetStackingModifier, GetAppliedCount, MoveNext, Cleanup counter, lookup timing) not installed");
                return;
            }

            foreach (var m in AccessTools.GetDeclaredMethods(typeof(UpgradeProperty)))
            {
                if (m.Name == nameof(UpgradeProperty.GetStackingModifier))
                    Patch(harmony, m, prefix: nameof(Stacking_Prefix), finalizer: nameof(Stacking_Finalizer));
            }
            Patch(harmony, AccessTools.Method(typeof(UpgradeProperty), nameof(UpgradeProperty.GetAppliedCount)),
                prefix: nameof(Applied_Prefix), finalizer: nameof(Applied_Finalizer));

            Patch(harmony, AccessTools.Method(typeof(PlayerData.EquippedUpgradeEnumerator), nameof(PlayerData.EquippedUpgradeEnumerator.MoveNext)),
                prefix: nameof(MoveNext_Prefix));
            Patch(harmony, gearData == null ? null : AccessTools.Method(gearData, "Cleanup"),
                prefix: nameof(Cleanup_Prefix));
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase target,
            string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
        {
            if (target == null)
            {
                Log.LogWarning("Patch target not found (prefix=" + prefix + ")");
                return;
            }
            string name = target.DeclaringType?.Name + "." + target.Name + "(" + target.GetParameters().Length + ")";
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Patches), finalizer),
                    transpiler: transpiler == null ? null : new HarmonyMethod(typeof(Patches), transpiler),
                    ilmanipulator: null);
                Log.LogInfo("Patched " + name);
            }
            catch (Exception e)
            {
                Log.LogError("Failed to patch " + name + ": " + e);
            }
        }

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

        private static bool Lookup_Prefix(int id, ref UpgradeInstance __result, out LookupState __state)
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

        private static void Lookup_Postfix(int id, UpgradeInstance __result, LookupState __state)
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

        private static string Describe(UpgradeInstance inst)
        {
            if (inst == null) return "null";
            try { return "#" + inst.InstanceID + " " + inst.Upgrade?.name; }
            catch { return "#?"; }
        }

        // ---------------- ApplyUpgrades call sites ----------------

        private static readonly MethodInfo ApplyUpgradesMethod = AccessTools.Method(typeof(IGear), nameof(IGear.ApplyUpgrades));
        private static readonly MethodInfo TimedApplyUpgradesMethod = AccessTools.Method(typeof(Patches), nameof(TimedApplyUpgrades));

        // Call sites of IGear.ApplyUpgrades in Assembly-CSharp (game v2.0.4, from the IL dump).
        private static IEnumerable<MethodBase> ApplyUpgradesCallers()
        {
            string[] typeAndMethod =
            {
                "GearUtil:ReapplyUpgrades",
                "Gun:Setup",
                "MycoGear:Setup",
                "ScrapperGear:Setup",
                "Throwable:Setup",
                "Wingsuit:Setup",
                "EquipGearInteractable:EquipGear_ClientRpc",
            };
            foreach (var tm in typeAndMethod)
            {
                var parts = tm.Split(':');
                var type = AccessTools.TypeByName(parts[0]);
                if (type == null) { Log.LogWarning("ApplyUpgrades caller type not found: " + parts[0]); continue; }
                foreach (var m in AccessTools.GetDeclaredMethods(type))
                    if (m.Name == parts[1] && !m.IsAbstract) yield return m;
            }

            var menu = AccessTools.TypeByName("Menu");
            var close = menu == null ? null : AccessTools.Method(menu, "CloseCoroutine");
            var moveNext = close == null ? null : AccessTools.EnumeratorMoveNext(close);
            if (moveNext != null) yield return moveNext;
            else Log.LogWarning("Menu.CloseCoroutine MoveNext not found");
        }

        private static IEnumerable<CodeInstruction> RedirectApplyUpgrades_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
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

        private static void Reapply_Prefix(out OpState __state) => __state = BeginOp(ref _reapplyDepth);

        private static Exception Reapply_Finalizer(Exception __exception, OpState __state)
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

        internal static string GearName(IUpgradable gear)
        {
            try { return gear?.Info?.Name ?? "?"; }
            catch { return "?"; }
        }

        // ---------------- Hot callers ----------------

        private static int _stackDepth;
        private static int _appliedDepth;

        private static void Stacking_Prefix(out long __state)
            => __state = _stackDepth++ == 0 ? Stopwatch.GetTimestamp() : 0;

        private static Exception Stacking_Finalizer(Exception __exception, long __state)
        {
            if (--_stackDepth == 0)
            {
                Stats.StackingCalls++;
                Stats.StackingTicks += Stopwatch.GetTimestamp() - __state;
            }
            return __exception;
        }

        private static void Applied_Prefix(out long __state)
            => __state = _appliedDepth++ == 0 ? Stopwatch.GetTimestamp() : 0;

        private static Exception Applied_Finalizer(Exception __exception, long __state)
        {
            if (--_appliedDepth == 0)
            {
                Stats.AppliedCountCalls++;
                Stats.AppliedCountTicks += Stopwatch.GetTimestamp() - __state;
            }
            return __exception;
        }

        private static void MoveNext_Prefix() => Stats.EnumMoveNext++;

        private static void Cleanup_Prefix() => Stats.GearDataCleanup++;
    }
}
