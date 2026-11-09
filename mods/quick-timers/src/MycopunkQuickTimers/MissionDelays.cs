using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unity.Netcode;

namespace MycopunkQuickTimers
{
    internal static class MissionDelays
    {
        private static bool _statsSyncAvailable;
        private static bool _statsReceived;
        private static bool _extractionFaulted;

        internal static void Setup(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(StatsSyncPatch)).Patch();
                _statsSyncAvailable = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Mission end delay: stats synchronization unavailable; using original delay. " + e.Message); }
            Apply(harmony, typeof(DropPatch), nameof(DropPod.FakeWaitToContinueDrop));
            Apply(harmony, typeof(ExtractionPatch), nameof(DropPod.LaunchFromMissionCoroutine));
            Apply(harmony, typeof(StartPatch), nameof(MissionManager.StartFirstObjective));
            Apply(harmony, typeof(EndPatch), nameof(MissionManager.OnMissionCompleteCoroutine));
        }

        private static void Apply(Harmony harmony, Type patch, string coroutineName)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Mission delay " + coroutineName + " unchanged: " + e.Message); }
        }

        [HarmonyPatch]
        private static class StatsSyncPatch
        {
            [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.OnMissionCompleteCoroutine))]
            [HarmonyPrefix]
            private static void ResetStats() => _statsReceived = false;

            // The server broadcasts these stats only after collecting all players' mission data.
            [HarmonyPatch(typeof(DropPod), nameof(DropPod.SetMissionData_ClientRpc))]
            [HarmonyPostfix]
            private static void ReceiveStats() => _statsReceived = true;
        }

        [HarmonyPatch(typeof(DropPod), nameof(DropPod.FakeWaitToContinueDrop), MethodType.Enumerator)]
        private static class DropPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Drop(instructions);
        }

        [HarmonyPatch(typeof(DropPod), nameof(DropPod.LaunchFromMissionCoroutine), MethodType.Enumerator)]
        private static class ExtractionPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Extraction(instructions);
        }

        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartFirstObjective), MethodType.Enumerator)]
        private static class StartPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Start(instructions);
        }

        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.OnMissionCompleteCoroutine), MethodType.Enumerator)]
        private static class EndPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => End(instructions);
        }

        // CodeInstruction.labels is a List<Label> of 0Harmony's mscorlib, which netstandard2.1 cannot bind to (not even with nameof), so it is read
        // by reflection (at runtime both are the same mscorlib).
        private static readonly FieldInfo LabelsField = AccessTools.Field(typeof(CodeInstruction), "labels");
        private static bool HasLabels(CodeInstruction instruction) =>
            LabelsField == null || !(LabelsField.GetValue(instruction) is IList labels) || labels.Count != 0;

        private static bool IsLessThan(CodeInstruction instruction) => instruction.opcode == OpCodes.Blt || instruction.opcode == OpCodes.Blt_S;
        // The coroutines' locals are compiler-generated fields (<time>5__N), which C# cannot name.
        private static bool IsTimeField(FieldInfo field) => field.Name.StartsWith("<time>", StringComparison.Ordinal);
        private static bool IsTimeField(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldfld &&
            instruction.operand is FieldInfo field && IsTimeField(field);

        private static IEnumerable<CodeInstruction> Drop(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var overrideDuration = AccessTools.Method(typeof(Mission), nameof(Mission.OverrideDropDuration));
            var matches = Enumerable.Range(2, code.Count - 5).Where(i =>
                code[i].opcode == OpCodes.Ldc_R4 && Equals(code[i].operand, 6f) &&
                (code[i - 1].opcode == OpCodes.Brtrue || code[i - 1].opcode == OpCodes.Brtrue_S) &&
                code[i - 2].Calls(overrideDuration) &&
                (code[i + 1].opcode == OpCodes.Br || code[i + 1].opcode == OpCodes.Br_S) &&
                code[i + 3].opcode == OpCodes.Stfld && code[i + 3].operand is FieldInfo field && IsTimeField(field)).ToList();
            ReplaceConstant(code, matches, nameof(DropDuration), "drop");
            return code;
        }

        private static IEnumerable<CodeInstruction> Start(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var field = AccessTools.Field(typeof(MissionManager), nameof(MissionManager.timeBeforeStartingMission));
            var matches = Enumerable.Range(1, code.Count - 2).Where(i => field != null &&
                code[i].LoadsField(field) && IsLessThan(code[i + 1]) &&
                i >= 2 && IsTimeField(code[i - 2])).ToList();
            if (matches.Count != 1 || HasLabels(code[matches[0] + 1]))
            {
                Plugin.Log.LogWarning("Mission start delay: expected code not found; unchanged.");
                return code;
            }
            code.Insert(matches[0] + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MissionDelays), nameof(StartDuration))));
            return code;
        }

        private static IEnumerable<CodeInstruction> Extraction(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var field = AccessTools.Field(typeof(DropPod), nameof(DropPod.extractionTime));
            var matches = Enumerable.Range(2, code.Count - 3).Where(i => field != null &&
                code[i].LoadsField(field) && IsLessThan(code[i + 1]) && IsTimeField(code[i - 2])).ToList();
            if (matches.Count != 1 || HasLabels(code[matches[0] + 1]))
            {
                Plugin.Log.LogWarning("Extraction delay: expected code not found; unchanged.");
                return code;
            }
            code.Insert(matches[0] + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MissionDelays), nameof(ExtractionDuration))));
            return code;
        }

        private static IEnumerable<CodeInstruction> End(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var rewards = AccessTools.Field(typeof(Global), nameof(Global.rewardInfoBuffer));
            var matches = Enumerable.Range(1, code.Count - 3).Where(i =>
                code[i].opcode == OpCodes.Ldc_R4 && Equals(code[i].operand, 3.5f) &&
                IsTimeField(code[i - 1]) && IsLessThan(code[i + 1]) &&
                rewards != null && code[i + 2].LoadsField(rewards)).ToList();
            ReplaceConstant(code, matches, nameof(EndDuration), "end");
            return code;
        }

        private static void ReplaceConstant(List<CodeInstruction> code, List<int> matches, string method, string name)
        {
            if (matches.Count != 1) { Plugin.Log.LogWarning("Mission " + name + " delay: expected code not found; unchanged."); return; }
            code[matches[0]].opcode = OpCodes.Call;
            code[matches[0]].operand = AccessTools.Method(typeof(MissionDelays), method);
        }

        // A positive duration preserves at least one coroutine yield and the drop's terrain-generation wait.
        private static float DropDuration() => Plugin.FastDrop.Value ? 0.01f : 6f;
        private static float StartDuration(float original) => Plugin.FastMissionStart.Value ? Math.Min(original, 0.01f) : original;
        private static float ExtractionDuration(float original)
        {
            if (!Plugin.FastExtraction.Value || _extractionFaulted || !_statsSyncAvailable || !_statsReceived) return original;
            try
            {
                var network = NetworkManager.Singleton;
                var pod = DropPod.Instance;
                var players = GameManager.players;
                if (network == null || !network.IsServer || !network.IsListening || pod == null || players == null ||
                    players.Count == 0 || players.Count < network.ConnectedClientsList.Count) return original;
                foreach (var player in players)
                    if (player == null || !player.IsAlive || !pod.IsPlayerInside(player, false, 16f)) return original;
                return Math.Min(original, 1f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Extraction delay check failed; using original duration. " + e.Message);
                _extractionFaulted = true;
                return original;
            }
        }
        private static float EndDuration() => Plugin.FastMissionEnd.Value && _statsSyncAvailable && _statsReceived ? 0.01f : 3.5f;
    }
}
