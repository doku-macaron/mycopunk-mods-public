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
                var coroutine = AccessTools.Method(typeof(MissionManager), "OnMissionCompleteCoroutine");
                var statsRpc = AccessTools.Method(typeof(DropPod), "SetMissionData_ClientRpc");
                if (coroutine != null && statsRpc != null)
                {
                    harmony.Patch(coroutine, prefix: new HarmonyMethod(typeof(MissionDelays), nameof(ResetStats)));
                    harmony.Patch(statsRpc, postfix: new HarmonyMethod(typeof(MissionDelays), nameof(ReceiveStats)));
                    _statsSyncAvailable = true;
                }
                else Plugin.Log.LogWarning("Mission end delay: stats synchronization unavailable; using original delay.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Mission end delay: stats synchronization unavailable; using original delay. " + e.Message); }
            Patch(harmony, typeof(DropPod), "FakeWaitToContinueDrop", nameof(Drop));
            Patch(harmony, typeof(DropPod), "LaunchFromMissionCoroutine", nameof(Extraction));
            Patch(harmony, typeof(MissionManager), "StartFirstObjective", nameof(Start));
            Patch(harmony, typeof(MissionManager), "OnMissionCompleteCoroutine", nameof(End));
        }

        private static void Patch(Harmony harmony, Type type, string coroutineName, string transpiler)
        {
            try
            {
                var coroutine = AccessTools.Method(type, coroutineName);
                var moveNext = coroutine == null ? null : AccessTools.EnumeratorMoveNext(coroutine);
                if (moveNext == null) { Plugin.Log.LogWarning("Mission delay " + coroutineName + ": coroutine not found."); return; }
                harmony.Patch(moveNext, transpiler: new HarmonyMethod(typeof(MissionDelays), transpiler));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Mission delay " + coroutineName + " unchanged: " + e.Message); }
        }

        private static bool IsLessThan(CodeInstruction instruction) => instruction.opcode == OpCodes.Blt || instruction.opcode == OpCodes.Blt_S;
        private static bool IsTimeField(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldfld &&
            instruction.operand is FieldInfo field && field.Name.StartsWith("<time>", StringComparison.Ordinal);

        private static IEnumerable<CodeInstruction> Drop(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var overrideDuration = AccessTools.Method(typeof(Mission), "OverrideDropDuration");
            var matches = Enumerable.Range(2, code.Count - 5).Where(i =>
                code[i].opcode == OpCodes.Ldc_R4 && Equals(code[i].operand, 6f) &&
                (code[i - 1].opcode == OpCodes.Brtrue || code[i - 1].opcode == OpCodes.Brtrue_S) &&
                code[i - 2].Calls(overrideDuration) &&
                (code[i + 1].opcode == OpCodes.Br || code[i + 1].opcode == OpCodes.Br_S) &&
                code[i + 3].opcode == OpCodes.Stfld && code[i + 3].operand is FieldInfo field &&
                field.Name.StartsWith("<time>", StringComparison.Ordinal)).ToList();
            ReplaceConstant(code, matches, nameof(DropDuration), "drop");
            return code;
        }

        private static IEnumerable<CodeInstruction> Start(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var field = AccessTools.Field(typeof(MissionManager), "timeBeforeStartingMission");
            var matches = Enumerable.Range(1, code.Count - 2).Where(i => field != null &&
                code[i].LoadsField(field) && IsLessThan(code[i + 1]) &&
                i >= 2 && IsTimeField(code[i - 2])).ToList();
            var labelsField = AccessTools.Field(typeof(CodeInstruction), "labels");
            if (matches.Count != 1 || labelsField == null ||
                !(labelsField.GetValue(code[matches[0] + 1]) is IList labels) || labels.Count != 0)
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
            var field = AccessTools.Field(typeof(DropPod), "extractionTime");
            var matches = Enumerable.Range(2, code.Count - 3).Where(i => field != null &&
                code[i].LoadsField(field) && IsLessThan(code[i + 1]) && IsTimeField(code[i - 2])).ToList();
            var labelsField = AccessTools.Field(typeof(CodeInstruction), "labels");
            if (matches.Count != 1 || labelsField == null ||
                !(labelsField.GetValue(code[matches[0] + 1]) is IList labels) || labels.Count != 0)
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
            var rewards = AccessTools.Field(typeof(Global), "rewardInfoBuffer");
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
        private static void ResetStats() => _statsReceived = false;
        // The server broadcasts these stats only after collecting all players' mission data.
        private static void ReceiveStats() => _statsReceived = true;
        private static float EndDuration() => Plugin.FastMissionEnd.Value && _statsSyncAvailable && _statsReceived ? 0.01f : 3.5f;
    }
}
