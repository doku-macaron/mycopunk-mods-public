using System;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace MycopunkHealthNumbers
{
    /// <summary>
    /// Records the local player's temporary max-health cap (Player.tempMaxHealthCap).
    /// The cap is built up during a frame (MycoGear.Update multiplies it by 1 - consumedMaxHealth;
    /// some objectives do the same) and Player.UpdateTarget uses it and then resets it to 1.
    /// Reading the field from OnGUI would therefore always give 1, so it is read in a prefix on UpdateTarget.
    /// </summary>
    internal static class CapTracker
    {
        private static float _cap = 1f;
        private static int _frame = -1;

        public static bool Installed { get; private set; }

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(UpdateTargetPatch)).Patch();
                Installed = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook Player.UpdateTarget; the max-health cap is not shown. {e}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateTarget))]
        private static class UpdateTargetPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance)
            {
                if (!ReferenceEquals(__instance, Player.LocalPlayer))
                    return;
                _cap = __instance.tempMaxHealthCap;
                _frame = Time.frameCount;
            }
        }

        /// <summary>The cap as a fraction of max health (1 = no cap). Stale values (UpdateTarget not running) count as 1.</summary>
        public static float Current => Installed && Time.frameCount - _frame <= 2 ? _cap : 1f;
    }
}
