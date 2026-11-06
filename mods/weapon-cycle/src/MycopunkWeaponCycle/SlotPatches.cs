using HarmonyLib;
using Pigeon.Movement;
using UnityEngine.InputSystem;

namespace MycopunkWeaponCycle
{
    /// <summary>
    /// Puts the stored (heavy) weapon into the scroll wheel and the weapon 1 key.
    ///
    /// Player.OnSwitchSlot (wheel) and Player.OnSlot1Pressed (weapon 1 key) are replaced by prefixes that keep the
    /// game's checks (_switchGearSlotLocks, and a swap already running for the wheel) and its call,
    /// SwapToSlot(slot, null, false, 0.102), only with the slot from <see cref="WeaponCycle"/>. The weapon 2 key and
    /// the stored-slot key are untouched. Turned off in the MODS tab, the game's own code runs.
    /// </summary>
    internal static class SlotPatches
    {
        private const float StowDuration = 0.102f; // what the game passes from these handlers

        private static readonly AccessTools.FieldRef<Player, int> Locks =
            AccessTools.FieldRefAccess<Player, int>("_switchGearSlotLocks");
        private static readonly AccessTools.FieldRef<Player, System.Collections.IEnumerator> Switching =
            AccessTools.FieldRefAccess<Player, System.Collections.IEnumerator>("switchSlotCoroutine");

        private delegate void SwapToSlotFn(Player player, int slot, IGear gearToHold, bool unequipNow, float stowDuration);
        private static SwapToSlotFn _swap;

        public static void Install(Harmony harmony)
        {
            var swap = AccessTools.Method(typeof(Player), "SwapToSlot", new[] { typeof(int), typeof(IGear), typeof(bool), typeof(float) });
            var wheel = AccessTools.Method(typeof(Player), "OnSwitchSlot");
            var key1 = AccessTools.Method(typeof(Player), "OnSlot1Pressed");
            if (swap == null || wheel == null || key1 == null)
            {
                Plugin.Log.LogWarning("Player.SwapToSlot / OnSwitchSlot / OnSlot1Pressed not found; weapon cycle is off.");
                return;
            }
            _swap = AccessTools.MethodDelegate<SwapToSlotFn>(swap);
            var empty = AccessTools.PropertyGetter(typeof(Gun), "IsMagazineAndReserveEmpty");
            var noAmmo = AccessTools.PropertyGetter(typeof(Gun), "CanFireWithoutAmmo");
            if (empty != null && noAmmo != null)
            {
                _isEmpty = AccessTools.MethodDelegate<System.Func<Gun, bool>>(empty);
                _canFireWithoutAmmo = AccessTools.MethodDelegate<System.Func<Gun, bool>>(noAmmo);
            }
            else
                Plugin.Log.LogWarning("Gun.IsMagazineAndReserveEmpty / CanFireWithoutAmmo not found; the wheel does not skip empty weapons.");
            harmony.Patch(wheel, prefix: new HarmonyMethod(typeof(SlotPatches), nameof(Wheel_Prefix)));
            harmony.Patch(key1, prefix: new HarmonyMethod(typeof(SlotPatches), nameof(Weapon1_Prefix)));
        }

        private static bool Wheel_Prefix(Player __instance, InputAction.CallbackContext context)
        {
            if (!Plugin.Enabled.Value)
                return true;
            try
            {
                if (Locks(__instance) > 0 || Switching(__instance) != null)
                    return false;
                float v = context.ReadValue<float>();
                if (v == 0f)
                    return false;
                var player = __instance;
                int slot = Plugin.SkipEmpty.Value
                    ? WeaponCycle.Wheel(player.SelectedGearSlot, v > 0f, player.IsGearStored(), s => IsOutOfAmmo(player, s))
                    : WeaponCycle.Wheel(player.SelectedGearSlot, v > 0f, player.IsGearStored());
                if (slot != player.SelectedGearSlot) // the same slot: every other weapon is out of ammo, stay
                    _swap(__instance, slot, null, false, StowDuration);
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Weapon cycle (wheel) failed; using the game's: " + e);
                return true;
            }
        }

        /// <summary>
        /// A gun in the slot with both its magazine and reserve empty (the game's Gun.IsMagazineAndReserveEmpty,
        /// which PlateLauncher overrides), unless it can fire without ammo (Gun.CanFireWithoutAmmo:
        /// ScoutLaserRifle, PlateLauncher). Anything else, or an empty slot, is not skipped by this.
        /// </summary>
        private static bool IsOutOfAmmo(Player player, int slot)
        {
            var all = player.Gear;
            var gun = all != null && slot >= 1 && slot <= all.Length ? all[slot - 1] as Gun : null;
            return gun != null && !CanFireWithoutAmmo(gun) && IsEmpty(gun);
        }

        // Virtual getters called through the delegates, so the overrides (PlateLauncher, ScoutLaserRifle) apply.
        // Null when the game no longer has them: then nothing is skipped.
        private static System.Func<Gun, bool> _isEmpty, _canFireWithoutAmmo;
        private static bool IsEmpty(Gun gun) => _isEmpty != null && _isEmpty(gun);
        private static bool CanFireWithoutAmmo(Gun gun) => _canFireWithoutAmmo != null && _canFireWithoutAmmo(gun);

        private static bool Weapon1_Prefix(Player __instance)
        {
            if (!Plugin.Enabled.Value)
                return true;
            try
            {
                if (Locks(__instance) > 0)
                    return false;
                int slot = WeaponCycle.Weapon1Key(__instance.SelectedGearSlot, __instance.IsGearStored());
                _swap(__instance, slot, null, false, StowDuration);
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Weapon cycle (weapon 1 key) failed; using the game's: " + e);
                return true;
            }
        }
    }
}
