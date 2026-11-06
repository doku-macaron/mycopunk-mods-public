using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MycopunkQuickTimers
{
    internal static class StartupLogos
    {
        internal static void Setup(Harmony harmony)
        {
            try
            {
                var coroutine = AccessTools.Method(typeof(StartMenu), "InitializeCoroutine");
                var moveNext = coroutine != null ? AccessTools.EnumeratorMoveNext(coroutine) : null;
                if (moveNext == null) { Plugin.Log.LogWarning("Startup logos: coroutine not found."); return; }
                harmony.Patch(moveNext, transpiler: new HarmonyMethod(typeof(StartupLogos), nameof(Transpiler)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Startup logos unchanged: " + e.Message); }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var matches = new List<int>();
            var wipes = new List<int>();
            for (int i = 1; i + 3 < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldc_R4 && code[i].operand is float seconds &&
                    code[i - 1].opcode == OpCodes.Ldfld && code[i - 1].operand is FieldInfo field &&
                    field.Name.StartsWith("<time>", StringComparison.Ordinal) &&
                    (code[i + 1].opcode == OpCodes.Blt || code[i + 1].opcode == OpCodes.Blt_S))
                {
                    if (seconds == 3f) matches.Add(i);
                    else if (seconds == 1f) wipes.Add(i);
                }

            var logo = AccessTools.Field(typeof(StartMenu), "pigeonsAtPlayIcon");
            var secondLogo = AccessTools.Field(typeof(StartMenu), "devolverIcon");
            var pcFeatures = AccessTools.PropertyGetter(typeof(Global), "DisablePCFeatures");
            var banksLoaded = AccessTools.PropertyGetter(typeof(Global), "AreAllBanksLoaded");
            var wipe = AccessTools.Field(typeof(StartMenu), "initializeWipe");
            var unlockCursor = AccessTools.Method(typeof(PlayerInput), "UnlockCursor", Type.EmptyTypes);
            var labelsField = AccessTools.Field(typeof(CodeInstruction), "labels");
            var disableStartup = code.FindIndex(c => c.opcode == OpCodes.Ldstr && Equals(c.operand, "DisableStartup"));
            int branch = disableStartup + 9;
            int bankCheck = branch < code.Count ? code.FindIndex(branch + 1, c => c.Calls(banksLoaded)) : -1;
            int bankBranch = bankCheck + 1;
            int gap = matches.Count == 2 ? code.FindIndex(matches[0] + 1, matches[1] - matches[0] - 1,
                c => c.opcode == OpCodes.Ldc_R4 && c.operand is float seconds && seconds == 0.6f) : -1;
            if (matches.Count != 2 || wipes.Count != 2 || logo == null || secondLogo == null || pcFeatures == null || banksLoaded == null ||
                wipe == null || unlockCursor == null || !code[wipes[0] + 2].Calls(unlockCursor) || !code[wipes[1] + 3].LoadsField(wipe) ||
                !code[matches[0] + 3].LoadsField(logo) || !code[matches[1] + 2].Calls(pcFeatures) ||
                gap < 1 || gap + 3 >= code.Count || !code[gap + 3].LoadsField(secondLogo) ||
                code[gap - 1].opcode != OpCodes.Ldfld || !(code[gap - 1].operand is FieldInfo gapTime) ||
                !gapTime.Name.StartsWith("<time>", StringComparison.Ordinal) ||
                (code[gap + 1].opcode != OpCodes.Blt && code[gap + 1].opcode != OpCodes.Blt_S) ||
                disableStartup < 2 || branch >= code.Count || labelsField == null || !code[disableStartup - 2].Calls(banksLoaded) ||
                code[branch - 1].opcode != OpCodes.And ||
                (code[branch].opcode != OpCodes.Brtrue && code[branch].opcode != OpCodes.Brtrue_S) ||
                code[branch].operand == null ||
                bankCheck < 0 || bankBranch >= code.Count || labelsField.GetValue(code[bankCheck]) is not IList bankLabels ||
                !bankLabels.Contains(code[branch].operand) ||
                (code[bankBranch].opcode != OpCodes.Brtrue && code[bankBranch].opcode != OpCodes.Brtrue_S) ||
                code[bankBranch].operand == null ||
                !code.Skip(bankBranch + 1).Select((instruction, index) => new { instruction, index })
                    .Any(x => labelsField.GetValue(x.instruction) is IList labels && labels.Contains(code[bankBranch].operand) &&
                        bankBranch + 2 + x.index < code.Count && code[bankBranch + 2 + x.index].LoadsField(wipe)))
            {
                Plugin.Log.LogWarning("Startup sequence: expected code not found; unchanged.");
                return code;
            }
            foreach (int index in matches) code[index].operand = 0f;
            foreach (int index in wipes) code[index].operand = 0f;
            code[gap].operand = 0f;
            object bankTarget = code[bankBranch].operand;
            code[bankBranch].opcode = OpCodes.Pop;
            code[bankBranch].operand = null;
            code.Insert(bankBranch + 1, new CodeInstruction(OpCodes.Br, bankTarget));
            object target = code[branch].operand;
            code[branch].opcode = OpCodes.Pop;
            code[branch].operand = null;
            code.Insert(branch + 1, new CodeInstruction(OpCodes.Br, target));
            Plugin.Log.LogInfo("Startup sequence: showing the title while sound banks load in the background.");
            return code;
        }
    }
}
