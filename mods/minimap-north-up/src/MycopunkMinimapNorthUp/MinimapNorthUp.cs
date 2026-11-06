using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace MycopunkMinimapNorthUp
{
    /// <summary>
    /// North-up corner minimap.
    ///
    /// Minimap.UpdateMinimap turns the map image by the camera yaw (Quaternion.Euler(0, 0, yaw), local 3, which is
    /// also passed by ref to UpdatePing so every icon is placed with it) and keeps the player arrow pointing up.
    /// Toward full screen it lerps the map rotation to identity and the arrow to Euler(0, 0, -yaw); the full-screen
    /// map is north-up already. With Minimap.NorthUp on, this does the full-screen thing at every size:
    ///   1. the first Quaternion.Euler (the map rotation)          -> identity
    ///   2. "playerPointer.rotation = identity" (corner-map branch) -> Euler(0, 0, -yaw)
    ///   3. "playerPointer.rotation = LerpUnclamped(identity, Euler(0, 0, -yaw), blend)" -> Euler(0, 0, -yaw)
    /// Each replacement checks the setting every frame, so it can be switched in game. If any of the three is not
    /// found (game update), nothing is changed.
    /// </summary>
    internal static class MinimapNorthUp
    {
        private static readonly MethodInfo Euler = AccessTools.Method(typeof(Quaternion), nameof(Quaternion.Euler),
            new[] { typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo Identity = AccessTools.PropertyGetter(typeof(Quaternion), nameof(Quaternion.identity));
        private static readonly MethodInfo Lerp = AccessTools.Method(typeof(Quaternion), nameof(Quaternion.LerpUnclamped),
            new[] { typeof(Quaternion), typeof(Quaternion), typeof(float) });
        private static readonly MethodInfo SetLocalRotation = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.localRotation));
        private static readonly FieldInfo PlayerPointer = AccessTools.Field(typeof(Minimap), "playerPointer");

        internal static bool Active;

        public static void Install(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(Minimap), "UpdateMinimap");
            if (target == null || PlayerPointer == null || Euler == null || Identity == null || Lerp == null || SetLocalRotation == null)
            {
                Plugin.Log.LogWarning("Minimap.UpdateMinimap or its fields not found; north-up minimap is off.");
                return;
            }
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(MinimapNorthUp), nameof(Transpiler)));
            if (Active)
                Plugin.Log.LogInfo("North-up minimap patch installed.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();

            // Find all three places first and change nothing unless every one is there.
            // 1. The map rotation: the first Quaternion.Euler(0, 0, yaw).
            int map = list.FindIndex(c => c.Calls(Euler));
            if (map < 0)
                return Fail(list, "map rotation");

            int idle = -1, blend = -1;
            for (int k = 0; k < list.Count - 3; k++)
            {
                if (!list[k].LoadsField(PlayerPointer))
                    continue;
                // 2. playerPointer.rectTransform.localRotation = Quaternion.identity
                if (idle < 0 && list[k + 2].Calls(Identity) && list[k + 3].Calls(SetLocalRotation))
                {
                    idle = k + 2;
                    continue;
                }
                // 3. playerPointer.rectTransform.localRotation = Quaternion.LerpUnclamped(identity, Euler(0, 0, -yaw), blend)
                for (int j = k + 2; blend < 0 && j < list.Count - 1 && j < k + 16; j++)
                {
                    if (list[j].Calls(Lerp) && list[j + 1].Calls(SetLocalRotation))
                        blend = j;
                }
            }
            if (idle < 0)
                return Fail(list, "player arrow (corner map)");
            if (blend < 0)
                return Fail(list, "player arrow (full-screen blend)");

            // Same signatures and return types, so only the operands change (no labels to move).
            list[map].operand = AccessTools.Method(typeof(MinimapNorthUp), nameof(MapRotation));
            list[blend].operand = AccessTools.Method(typeof(MinimapNorthUp), nameof(PointerBlend));
            list[idle].operand = AccessTools.Method(typeof(MinimapNorthUp), nameof(PointerIdle));
            Active = true;
            return list;
        }

        private static IEnumerable<CodeInstruction> Fail(List<CodeInstruction> list, string what)
        {
            Plugin.Log.LogWarning("North-up minimap: " + what + " not found in Minimap.UpdateMinimap (game update?); north-up is off.");
            return list;
        }

        private static bool On => Plugin.NorthUp != null && Plugin.NorthUp.Value;

        private static Quaternion MapRotation(float x, float y, float z) =>
            On ? Quaternion.identity : Quaternion.Euler(x, y, z);

        // Replaces Quaternion.identity (no arguments), so it reads the yaw from where UpdateMinimap reads it.
        private static Quaternion PointerIdle() =>
            On ? Quaternion.Euler(0f, 0f, -PlayerLook.Rotation.eulerAngles.y) : Quaternion.identity;

        private static Quaternion PointerBlend(Quaternion a, Quaternion b, float t) =>
            On ? b : Quaternion.LerpUnclamped(a, b, t);
    }
}
