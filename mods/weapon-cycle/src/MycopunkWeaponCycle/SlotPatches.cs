using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine.InputSystem;

namespace MycopunkWeaponCycle
{
    /// <summary>
    /// Puts the stored (heavy) weapon into the scroll wheel and the weapon 1 key.
    ///
    /// Player.OnSwitchSlot (wheel) and Player.OnSlot1Pressed (weapon 1 key) keep the game's own code (its checks
    /// and its call SwapToSlot(slot, null, false, 0.102)); a transpiler only replaces the slot passed to SwapToSlot
    /// with the one from <see cref="WeaponCycle"/>. When the wheel would pick the slot already held (every other
    /// weapon is out of ammo), the swap is skipped. The weapon 2 key and the stored-slot key are untouched. Turned
    /// off in the MODS tab, the game's slot is passed through.
    /// </summary>
    internal static class SlotPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.OnSwitchSlot))]
        internal static class WheelPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
                ReplaceSlot(instructions, generator, true);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSlot1Pressed))]
        internal static class Weapon1Patch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
                ReplaceSlot(instructions, generator, false);
        }

        // Reflection: Harmony's Label type comes from Unity's mscorlib and cannot be referenced by netstandard here,
        // so the labels list is moved through this field.
        private static readonly System.Reflection.FieldInfo LabelsField = AccessTools.Field(typeof(CodeInstruction), "labels");

        private static IEnumerable<CodeInstruction> ReplaceSlot(IEnumerable<CodeInstruction> instructions, ILGenerator generator, bool wheel)
        {
            var code = instructions.ToList();
            var swap = AccessTools.Method(typeof(Player), nameof(Player.SwapToSlot), new[] { typeof(int), typeof(IGear), typeof(bool), typeof(float) });
            // The game's call: SwapToSlot(slot, null, false, 0.102f). The slot is already on the stack before the null.
            var matches = Enumerable.Range(3, Math.Max(0, code.Count - 3)).Where(i => swap != null && code[i].Calls(swap) &&
                code[i - 3].opcode == OpCodes.Ldnull && code[i - 2].opcode == OpCodes.Ldc_I4_0 &&
                code[i - 1].opcode == OpCodes.Ldc_R4).ToList();
            if (matches.Count != 1 || LabelsField == null || code[matches[0] - 3].blocks.Count != 0)
            {
                Plugin.Log.LogWarning("Weapon cycle: expected " + (wheel ? "wheel" : "weapon 1") + " code not found; unchanged.");
                return code;
            }

            int index = matches[0] - 3;
            var injected = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldarg_0) };
            var originalLabels = (IList)LabelsField.GetValue(code[index]);
            var injectedLabels = (IList)LabelsField.GetValue(injected[0]);
            foreach (var label in originalLabels) injectedLabels.Add(label);
            originalLabels.Clear();
            if (wheel) injected.Add(new CodeInstruction(OpCodes.Ldarg_1));
            injected.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SlotPatches), wheel ? nameof(WheelSlot) : nameof(Weapon1Slot))));
            if (wheel)
            {
                // Stack: this, slot. When the slot is the one already held, drop both and return.
                var proceed = generator.DefineLabel();
                injected.Add(new CodeInstruction(OpCodes.Dup));
                injected.Add(new CodeInstruction(OpCodes.Ldarg_0));
                injected.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SlotPatches), nameof(SkipWheelSwap))));
                injected.Add(new CodeInstruction(OpCodes.Brfalse, proceed));
                injected.Add(new CodeInstruction(OpCodes.Pop));
                injected.Add(new CodeInstruction(OpCodes.Pop));
                injected.Add(new CodeInstruction(OpCodes.Ret));
                originalLabels.Add(proceed);
            }
            code.InsertRange(index, injected);
            return code;
        }

        private static int WheelSlot(int original, Player player, InputAction.CallbackContext context)
        {
            if (!Plugin.Enabled.Value) return original;
            try
            {
                float value = context.ReadValue<float>();
                return Plugin.SkipEmpty.Value
                    ? WeaponCycle.Wheel(player.SelectedGearSlot, value > 0f, player.IsGearStored(), slot => IsOutOfAmmo(player, slot))
                    : WeaponCycle.Wheel(player.SelectedGearSlot, value > 0f, player.IsGearStored());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Weapon cycle (wheel) failed; using the game's: " + e); return original; }
        }

        private static bool SkipWheelSwap(int slot, Player player) => Plugin.Enabled.Value && slot == player.SelectedGearSlot;

        private static int Weapon1Slot(int original, Player player)
        {
            if (!Plugin.Enabled.Value) return original;
            try { return WeaponCycle.Weapon1Key(player.SelectedGearSlot, player.IsGearStored()); }
            catch (Exception e) { Plugin.Log.LogWarning("Weapon cycle (weapon 1 key) failed; using the game's: " + e); return original; }
        }

        /// <summary>
        /// A gun in the slot with both its magazine and reserve empty (the game's Gun.IsMagazineAndReserveEmpty,
        /// which PlateLauncher overrides), unless it can fire without ammo (Gun.CanFireWithoutAmmo:
        /// ScoutLaserRifle, PlateLauncher). Both are virtual, so the overrides apply. Anything else, or an empty
        /// slot, is not skipped by this.
        /// </summary>
        private static bool IsOutOfAmmo(Player player, int slot)
        {
            var all = player.Gear;
            var gun = all != null && slot >= 1 && slot <= all.Length ? all[slot - 1] as Gun : null;
            return gun != null && !gun.CanFireWithoutAmmo && gun.IsMagazineAndReserveEmpty;
        }
    }
}
