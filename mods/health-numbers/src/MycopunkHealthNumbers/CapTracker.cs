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
        private static AccessTools.FieldRef<Player, float> _capRef;
        private static float _cap = 1f;
        private static int _frame = -1;

        public static bool Installed { get; private set; }

        public static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Player), "UpdateTarget");
                if (target == null || AccessTools.Field(typeof(Player), "tempMaxHealthCap") == null)
                {
                    Plugin.Log.LogWarning("Player.UpdateTarget or Player.tempMaxHealthCap was not found; the max-health cap is not shown.");
                    return;
                }
                _capRef = AccessTools.FieldRefAccess<Player, float>("tempMaxHealthCap");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(CapTracker), nameof(Prefix)));
                Installed = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook Player.UpdateTarget; the max-health cap is not shown. {e}");
            }
        }

        private static void Prefix(Player __instance)
        {
            if (!ReferenceEquals(__instance, Player.LocalPlayer))
                return;
            _cap = _capRef(__instance);
            _frame = Time.frameCount;
        }

        /// <summary>The cap as a fraction of max health (1 = no cap). Stale values (UpdateTarget not running) count as 1.</summary>
        public static float Current => Installed && Time.frameCount - _frame <= 2 ? _cap : 1f;
    }
}
