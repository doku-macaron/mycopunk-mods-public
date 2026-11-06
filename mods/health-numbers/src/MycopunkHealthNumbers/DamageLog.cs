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

        private static FieldInfo _damage, _effect, _effectAmount, _flags, _guard, _beforeTakeDamage;
        private static string _lastHandlers;
        private const int IgnoresGuardFlags = 32 | 512; // DamageData.IgnoreDeathProtection: Overclocked | IgnoreDeathProtection
        private static MethodInfo _isDot;

        // Set while ApplyDamage_Owner runs for the local player; SetHealth_Owner records the new health here.
        private static bool _inDamage;
        private static float _newHealth;
        private static bool _healthSet;
        private static bool _guardBefore;

        public static void Install(Harmony harmony)
        {
            try
            {
                MethodInfo apply = AccessTools.Method(typeof(Player), "ApplyDamage_Owner");
                MethodInfo setHealth = AccessTools.Method(typeof(Player), "SetHealth_Owner");
                if (apply == null || setHealth == null)
                {
                    Plugin.Log.LogWarning("Player.ApplyDamage_Owner or SetHealth_Owner was not found; the damage log is off.");
                    return;
                }
                Type dd = apply.GetParameters()[0].ParameterType;
                _damage = AccessTools.Field(dd, "damage");
                _effect = AccessTools.Field(dd, "effect");
                _effectAmount = AccessTools.Field(dd, "effectAmount");
                _flags = AccessTools.Field(dd, "damageFlags");
                _isDot = AccessTools.PropertyGetter(dd, "IsDOT");

                harmony.Patch(apply,
                    prefix: new HarmonyMethod(typeof(DamageLog), nameof(ApplyPrefix)),
                    finalizer: new HarmonyMethod(typeof(DamageLog), nameof(ApplyFinalizer)));
                harmony.Patch(setHealth, prefix: new HarmonyMethod(typeof(DamageLog), nameof(SetHealthPrefix)));

                _guard = AccessTools.Field(typeof(Player), "hasDeathProtection");
                _beforeTakeDamage = AccessTools.Field(typeof(Player), "OnBeforeTakeDamage");
                MethodInfo heal = AccessTools.Method(typeof(Player), "ApplyHeal_Owner");
                if (heal != null && _guard != null)
                    harmony.Patch(heal,
                        prefix: new HarmonyMethod(typeof(DamageLog), nameof(HealPrefix)),
                        postfix: new HarmonyMethod(typeof(DamageLog), nameof(HealPostfix)));
                else
                    Plugin.Log.LogWarning("Player.hasDeathProtection or ApplyHeal_Owner was not found; the damage log does not follow the one-hit guard.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook the damage path; the damage log is off. {e}");
            }
        }

        private static void ApplyPrefix(Player __instance, object[] __args, out float __state)
        {
            __state = float.NaN;
            if (!Enabled() || !ReferenceEquals(__instance, Player.LocalPlayer) || !__instance.IsAlive)
                return;
            __state = __instance.Health;
            _inDamage = true;
            _healthSet = false;
            _guardBefore = Guard(__instance);
            LogHandlers(__instance);

            object data = __args[0];
            object source = __args.Length > 1 ? __args[1] : null;
            object flags = _flags?.GetValue(data);
            bool ignoresGuard = flags != null && (Convert.ToInt32(flags, CultureInfo.InvariantCulture) & IgnoresGuardFlags) != 0;
            Plugin.Log.LogInfo(
                $"[damage] t={Time.time:0.00} raw={F(_damage?.GetValue(data))} " +
                $"effect={_effect?.GetValue(data)} effectAmount={F(_effectAmount?.GetValue(data))} " +
                $"dot={_isDot?.Invoke(data, null)} flags={flags} " +
                $"source={DescribeSource(source)} hp={F(__state)}/{F(__instance.MaxHealth)} " +
                $"guard={(_guard == null ? "?" : _guardBefore ? "on" : "off")}{(ignoresGuard ? " (this hit ignores the guard)" : "")}");
        }

        private static bool Guard(Player p) => _guard != null && (bool)_guard.GetValue(p);

        /// <summary>The handlers on Player.OnBeforeTakeDamage (mission modifiers, upgrades), when they change.</summary>
        private static void LogHandlers(Player p)
        {
            if (_beforeTakeDamage == null)
                return;
            var d = _beforeTakeDamage.GetValue(p) as Delegate;
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

        private static void HealPrefix(Player __instance, out bool __state) =>
            __state = Enabled() && ReferenceEquals(__instance, Player.LocalPlayer) && Guard(__instance);

        private static void HealPostfix(Player __instance, bool __state)
        {
            if (!Enabled() || !ReferenceEquals(__instance, Player.LocalPlayer))
                return;
            if (!__state && Guard(__instance))
                Plugin.Log.LogInfo($"[guard] t={Time.time:0.00} back on (health {F(__instance.Health)}/{F(__instance.MaxHealth)})");
        }

        private static void SetHealthPrefix(Player __instance, float __0)
        {
            if (_inDamage && ReferenceEquals(__instance, Player.LocalPlayer))
            {
                _newHealth = __0;
                _healthSet = true;
            }
        }

        private static void ApplyFinalizer(Player __instance, float __state)
        {
            if (float.IsNaN(__state))
                return;
            _inDamage = false;
            bool guardAfter = Guard(__instance);
            string guard = _guard == null ? "" : $" guard {(_guardBefore ? "on" : "off")} -> {(guardAfter ? "on" : "off")}";
            if (_healthSet)
                Plugin.Log.LogInfo($"[damage]   lost={F(__state - _newHealth)} hp {F(__state)} -> {F(_newHealth)}{guard}");
            else
                Plugin.Log.LogInfo("[damage]   no health change (blocked, mitigated or deferred)" + guard);
            if (_guardBefore && !guardAfter)
                Plugin.Log.LogInfo($"[guard] t={Time.time:0.00} used: the hit left {F(_newHealth)}");
            if (_healthSet && _newHealth <= 0f)
                Plugin.Log.LogInfo($"[guard] t={Time.time:0.00} killed (guard was {(_guardBefore ? "on" : "off")} before the hit)");
        }

        private static string DescribeSource(object source)
        {
            if (source == null)
                return "null";
            string s = source.GetType().Name;
            if (source is Component c && c != null)
                s += $"({c.gameObject.name})";
            try
            {
                MethodInfo getBase = source.GetType().GetMethod("GetBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                     ?? FindInterfaceMethod(source.GetType(), "GetBase");
                object b = getBase?.Invoke(source, null);
                if (b != null && !ReferenceEquals(b, source))
                    s += " base=" + b.GetType().Name;
            }
            catch
            {
                // Best effort only.
            }
            return s;
        }

        private static MethodInfo FindInterfaceMethod(Type t, string name)
        {
            foreach (Type i in t.GetInterfaces())
            {
                MethodInfo m = i.GetMethod(name, Type.EmptyTypes);
                if (m != null)
                    return m;
            }
            return null;
        }

        private static string F(object v) =>
            v is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
    }
}
