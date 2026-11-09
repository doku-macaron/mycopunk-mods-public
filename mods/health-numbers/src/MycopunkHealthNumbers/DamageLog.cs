using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace MycopunkHealthNumbers
{
    /// <summary>
    /// Logs every hit the local player takes: the incoming DamageData (before the player's own modifiers),
    /// the source, and the health it actually removed. Comparing "raw" with "lost" shows which multipliers
    /// (e.g. elemental resistances) really apply.
    ///
    /// Since 0.4.2 it also follows the game's one-hit guard (Player.hasDeathProtection, measured for the user's
    /// "Unhealthy Charging" question): ApplyDamage_Owner lets a hit that would take all the remaining health leave
    /// 0.1 instead and clears the guard, unless the hit's flags include Overclocked (32) or IgnoreDeathProtection
    /// (512); ApplyHeal_Owner sets it again once health reaches the max. Each hit line shows the guard before and
    /// after and whether the hit ignores it, the handlers on Player.OnBeforeTakeDamage are listed when they change
    /// (mission modifiers such as TakeMoreDamageAndHealFaster_OnBeforeDamaged double the damage there), and a
    /// "[guard]" line marks when the guard is used, when it comes back and when a hit kills.
    /// </summary>
    internal static class DamageLog
    {
        public static Func<bool> Enabled = () => false;

        // Reflection: OnBeforeTakeDamage is an event, and C# only allows += / -= on it from outside Player, so its
        // backing delegate field (same name) is read this way.
        private static readonly FieldInfo BeforeTakeDamage = AccessTools.Field(typeof(Player), nameof(Player.OnBeforeTakeDamage));
        private static string _lastHandlers;
        private const int IgnoresGuardFlags = 32 | 512; // DamageData.IgnoreDeathProtection: Overclocked | IgnoreDeathProtection

        // Set while ApplyDamage_Owner runs for the local player; SetHealth_Owner records the new health here.
        private static bool _inDamage;
        private static float _newHealth;
        private static bool _healthSet;
        private static bool _guardBefore;

        public static void Install(Harmony harmony)
        {
            try
            {
                // Together: if one fails, the others are taken back, so the log is never half hooked.
                MycopunkShared.ModSetup.PatchTogether(harmony, typeof(ApplyDamagePatch), typeof(SetHealthPatch), typeof(HealPatch));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook the damage path; the damage log is off. {e}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ApplyDamage_Owner))]
        private static class ApplyDamagePatch
        {
            [HarmonyPrefix]
            // ref only so the analyzer does not read the field accesses as writes; the hit is not changed.
            private static void Prefix(Player __instance, ref DamageData data, IDamageSource source, out float __state)
            {
                __state = float.NaN;
                if (!Enabled() || !ReferenceEquals(__instance, Player.LocalPlayer) || !__instance.IsAlive)
                    return;
                __state = __instance.Health;
                _inDamage = true;
                _healthSet = false;
                _guardBefore = Guard(__instance);
                LogHandlers(__instance);

                DamageFlags flags = data.damageFlags;
                bool ignoresGuard = ((int)flags & IgnoresGuardFlags) != 0;
                Plugin.Log.LogInfo(
                    $"[damage] t={Time.time:0.00} raw={F(data.damage)} " +
                    $"effect={data.effect} effectAmount={F(data.effectAmount)} " +
                    $"dot={data.IsDOT} flags={flags} " +
                    $"source={DescribeSource(source)} hp={F(__state)}/{F(__instance.MaxHealth)} " +
                    $"guard={(_guardBefore ? "on" : "off")}{(ignoresGuard ? " (this hit ignores the guard)" : "")}");
            }

            [HarmonyFinalizer]
            private static void Finalizer(Player __instance, float __state)
            {
                if (float.IsNaN(__state))
                    return;
                _inDamage = false;
                bool guardAfter = Guard(__instance);
                string guard = $" guard {(_guardBefore ? "on" : "off")} -> {(guardAfter ? "on" : "off")}";
                if (_healthSet)
                    Plugin.Log.LogInfo($"[damage]   lost={F(__state - _newHealth)} hp {F(__state)} -> {F(_newHealth)}{guard}");
                else
                    Plugin.Log.LogInfo("[damage]   no health change (blocked, mitigated or deferred)" + guard);
                if (_guardBefore && !guardAfter)
                    Plugin.Log.LogInfo($"[guard] t={Time.time:0.00} used: the hit left {F(_newHealth)}");
                if (_healthSet && _newHealth <= 0f)
                    Plugin.Log.LogInfo($"[guard] t={Time.time:0.00} killed (guard was {(_guardBefore ? "on" : "off")} before the hit)");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetHealth_Owner))]
        private static class SetHealthPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance, float value)
            {
                if (_inDamage && ReferenceEquals(__instance, Player.LocalPlayer))
                {
                    _newHealth = value;
                    _healthSet = true;
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ApplyHeal_Owner))]
        private static class HealPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance, out bool __state) =>
                __state = Enabled() && ReferenceEquals(__instance, Player.LocalPlayer) && Guard(__instance);

            [HarmonyPostfix]
            private static void Postfix(Player __instance, bool __state)
            {
                if (!Enabled() || !ReferenceEquals(__instance, Player.LocalPlayer))
                    return;
                if (!__state && Guard(__instance))
                    Plugin.Log.LogInfo($"[guard] t={Time.time:0.00} back on (health {F(__instance.Health)}/{F(__instance.MaxHealth)})");
            }
        }

        private static bool Guard(Player p) => p.hasDeathProtection;

        /// <summary>The handlers on Player.OnBeforeTakeDamage (mission modifiers, upgrades), when they change.</summary>
        private static void LogHandlers(Player p)
        {
            if (BeforeTakeDamage == null)
                return;
            var d = BeforeTakeDamage.GetValue(p) as Delegate;
            string list = "none";
            if (d != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var h in d.GetInvocationList())
                    names.Add((h.Method.DeclaringType != null ? h.Method.DeclaringType.Name + "." : "") + h.Method.Name);
                list = string.Join(", ", names);
            }
            if (list == _lastHandlers)
                return;
            _lastHandlers = list;
            Plugin.Log.LogInfo("[damage] OnBeforeTakeDamage handlers: " + list);
        }

        private static string DescribeSource(IDamageSource source)
        {
            if (source == null)
                return "null";
            string s = source.GetType().Name;
            if (source is Component c && c != null)
                s += $"({c.gameObject.name})";
            try
            {
                IDamageSource b = source.GetBase();
                if (b != null && !ReferenceEquals(b, source))
                    s += " base=" + b.GetType().Name;
            }
            catch
            {
                // Best effort only.
            }
            return s;
        }

        private static string F(object v) =>
            v is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
    }
}
