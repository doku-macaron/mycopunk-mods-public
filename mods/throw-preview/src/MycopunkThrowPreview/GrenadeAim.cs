using System.Collections.Generic;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MycopunkThrowPreview
{
    /// <summary>
    /// Hold the grenade key to aim with a predicted arc, release to throw.
    ///
    /// The game throws on press: Player.OnThrowPressed (the Throw action's started/performed) calls Player.TryThrow,
    /// which swaps to the grenade (Gear[3], a Throwable); Throwable.Enable plays the throw animation, seen by everyone,
    /// and fires. Here TryThrow is held back while the Throw key is still down (only for a GrenadeGear with a charge
    /// that is not already out), and called again on release, so the whole throw, animation included, is the game's
    /// own and starts on release. A quick tap throws about as fast as before. With upgrades that use a held Throw
    /// (Player.OnThrowHeld), the game only calls TryThrow on a tap, which is already released, so they are unchanged.
    ///
    /// The arc is the game's own projectile step, IBullet.SimulateProjectile (fixed time step, BulletData.gravity),
    /// repeated from the bullet the throw would make: a copy of the grenade's GunData, PrepareFireData from the
    /// current view, GetBulletData, then ModifyBulletData, as Throwable.FireBullet does (spread left out). On a hit
    /// it bounces like SimpleProjectileBullet.Bounce (reflect, x0.75) while bounces are left, else it ends there,
    /// where GrenadeBullet.OnHit detonates. The fire point is not placed until the grenade is out, so its offset from
    /// the camera is taken from the last real throw (a small default before that).
    ///
    /// The bullet's speed, gravity, masks, bounces, range and lifetime are also taken from the last real bullet of the
    /// same grenade (SimpleProjectileBullet.Initialize, whose source is the Throwable), in case the bullet prefab or
    /// upgrades change them after GetBulletData. They differ a lot between grenades (measured: photon disc speed 100
    /// gravity 100; cryo 15 / 1; voltaic 98 / 25, surface mask 1), so they are kept per grenade. Each real throw logs the predicted and the real values side by side, to check the prediction.
    /// Grenades come with range.maxDamageRange = 0 (measured), so the range check of HandleCollisions is not used
    /// for them (several grenade bullets have their own HandleCollisions); the arc stops at the bullet's lifetime.
    /// </summary>
    internal static class GrenadeAim
    {
        private const BulletFlags OwnerFlags = (BulletFlags)11; // what Throwable.FireBullet passes for a normal shot
        private const int MaxSteps = 600;
        private const float DefaultLifetime = 10f;

        private static readonly AccessTools.FieldRef<Throwable, GunData> GunDataRef =
            AccessTools.FieldRefAccess<Throwable, GunData>("gunData");
        private static readonly AccessTools.FieldRef<Throwable, FireData> FireDataRef =
            AccessTools.FieldRefAccess<Throwable, FireData>("fireData");

        private static InputAction _throwAction;
        private static Player _aiming;
        private static bool _releasing;
        // Where the grenade leaves from, in camera space. Per kind of grenade, since each model has its own fire point
        // (measured: 1.2 m off for acid and 1.6 m for voltaic when the photon disc's was used). Before a grenade's
        // first throw, a fixed guess (another kind's offset was worse: it is not the same model).
        // Kept in BepInEx/ThrowPreview/fire-offsets.tsv by grenade name, so later sessions start right away.
        private static FireOffsetStore _offsets;
        private static readonly Vector3 DefaultFireOffset = new Vector3(0.05f, -0.25f, 0.03f); // typical of the measured ones (2026-11-04)
        private sealed class Learned
        {
            public BulletData Data;
            public float Lifetime;
        }

        // Per kind of grenade (its GearInfo): its last real bullet. Keyed by GearInfo, not by the Throwable, because
        // the game makes a new Throwable each time the grenade is switched (0.1.5 lost everything on a switch).
        private static readonly Dictionary<GearInfo, Learned> LearnedData = new Dictionary<GearInfo, Learned>();
        private static BulletData? _predicted; // the data of the last arc, for the log
        private static bool _predictedLearned;
        private static string _lastEnd = "";

        // The Lifetime property, called virtually: some bullets override it (IncendiaryGrenadeBullet: 1.5 s as a
        // cluster bomb), so the base field alone is not always what the bullet uses.
        private static readonly System.Reflection.MethodInfo LifetimeGetter =
            AccessTools.PropertyGetter(typeof(SimpleProjectileBullet), "Lifetime");

        private static float Lifetime(SimpleProjectileBullet bullet) =>
            LifetimeGetter != null ? (float)LifetimeGetter.Invoke(bullet, null) : 0f;
        private static readonly AccessTools.FieldRef<Player, LayerMask> GroundMask =
            AccessTools.FieldRefAccess<Player, LayerMask>("groundLayerMask");

        internal static bool Aiming => _aiming != null;

        public static void Install(Harmony harmony)
        {
            var pressed = AccessTools.Method(typeof(Player), "OnThrowPressed");
            var tryThrow = AccessTools.Method(typeof(Player), nameof(Player.TryThrow));
            var fireBullet = AccessTools.Method(typeof(Throwable), "FireBullet");
            var init = AccessTools.Method(typeof(SimpleProjectileBullet), nameof(SimpleProjectileBullet.Initialize));
            if (pressed == null || tryThrow == null || fireBullet == null || init == null)
            {
                Plugin.Log.LogWarning("Player.OnThrowPressed / TryThrow or Throwable.FireBullet not found; grenade aiming is off.");
                return;
            }
            harmony.Patch(pressed, prefix: new HarmonyMethod(typeof(GrenadeAim), nameof(OnThrowPressed_Prefix)));
            harmony.Patch(tryThrow, prefix: new HarmonyMethod(typeof(GrenadeAim), nameof(TryThrow_Prefix)));
            harmony.Patch(fireBullet, postfix: new HarmonyMethod(typeof(GrenadeAim), nameof(FireBullet_Postfix)));
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(GrenadeAim), nameof(BulletInitialize_Postfix)));
        }

        /// <summary>Keeps the real grenade bullet's data, and logs it next to the last prediction.</summary>
        private static void BulletInitialize_Postfix(SimpleProjectileBullet __instance, BulletData data, IDamageSource source, BulletFlags flags)
        {
            try
            {
                if (!(__instance is GrenadeBullet) || !flags.IsOwner() || !(source is GrenadeGear grenade))
                    return;
                float life = Lifetime(__instance);
                var info = ((IUpgradable)grenade).Info;
                if (info == null)
                    return;
                LearnedData[info] = new Learned { Data = data, Lifetime = life > 0f ? life : DefaultLifetime };
                if (_predicted is BulletData p)
                {
                    Plugin.Log.LogInfo("grenade thrown (" + __instance.GetType().Name + ", lifetime " + life.ToString("F1") + " s, prediction from " +
                        (_predictedLearned ? "its last throw" : "GunData") + "). predicted vs real: " +
                        "pos " + p.position.ToString("F2") + " / " + data.position.ToString("F2") +
                        ", dir " + p.direction.ToString("F3") + " / " + data.direction.ToString("F3") +
                        ", speed " + p.speed.ToString("F1") + " / " + data.speed.ToString("F1") +
                        ", gravity " + p.gravity.ToString("F2") + " / " + data.gravity.ToString("F2") +
                        ", bounces " + p.maxBounces + " / " + data.maxBounces +
                        ", surface mask " + p.surfaceCollisionMask + " / " + data.surfaceCollisionMask +
                        ", target mask " + p.targetCollisionMask + " / " + data.targetCollisionMask +
                        ", range " + p.range.maxDamageRange.ToString("F0") + " / " + data.range.maxDamageRange.ToString("F0") +
                        " | arc: " + _lastEnd);
                    _predicted = null;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not read the thrown grenade: " + e);
            }
        }

        private static void OnThrowPressed_Prefix(InputAction.CallbackContext context)
        {
            _throwAction = context.action;
        }

        private static bool TryThrow_Prefix(Player __instance)
        {
            try
            {
                if (_releasing || !Plugin.GrenadeAim.Value || __instance != Player.LocalPlayer)
                    return true;
                var grenade = Grenade(__instance);
                if (grenade == null || grenade.Active || ((IActivatedAbility)grenade).Charge < 1f)
                    return true; // not ours to hold back; the game plays its own error when uncharged
                if (_throwAction == null || !_throwAction.IsPressed())
                    return true; // released already (a tap): throw now
                _aiming = __instance;
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Grenade aiming failed; throwing normally: " + e);
                _aiming = null;
                return true;
            }
        }

        /// <summary>Remembers where the grenade leaves from, relative to the camera, for the next arc.</summary>
        private static void FireBullet_Postfix(Throwable __instance)
        {
            if (!(__instance is GrenadeGear) || __instance.Player != Player.LocalPlayer)
                return;
            var fd = FireDataRef(__instance);
            var info = ((IUpgradable)__instance).Info;
            if (info == null || _offsets == null)
                return;
            Vector3 o = Quaternion.Inverse(PlayerLook.Rotation) * (fd.firePosition - PlayerLook.Position);
            if (!_offsets.Set(info.name, o.x, o.y, o.z))
                return;
            try
            {
                _offsets.Save();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not save the grenade fire points: " + e.Message);
            }
        }

        /// <summary>Loads the fire points saved by earlier sessions.</summary>
        public static void LoadOffsets(string path)
        {
            _offsets = new FireOffsetStore(path);
            try
            {
                _offsets.Load();
                Plugin.Log.LogInfo("Grenade fire points: " + _offsets.Count + " known (" + path + ").");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not read the grenade fire points; starting without them: " + e.Message);
            }
        }

        private static Vector3 FireOffset(GearInfo info) =>
            info != null && _offsets != null && _offsets.TryGet(info.name, out float x, out float y, out float z)
                ? new Vector3(x, y, z)
                : DefaultFireOffset;

        /// <summary>Per frame: throw on release, cancel when it no longer applies, else fill the arc.</summary>
        /// <returns>True when the arc ends where the grenade would go off.</returns>
        public static void Tick()
        {
            var player = _aiming;
            if (player == null)
                return;
            var grenade = AimedGrenade(player);
            if (grenade == null)
            {
                _aiming = null;
                return;
            }
            if (_throwAction == null || !_throwAction.IsPressed())
            {
                _aiming = null;
                _releasing = true;
                try { player.TryThrow(); }
                finally { _releasing = false; }
            }
        }

        /// <summary>Fills the arc while aiming (called right before the UI is drawn, no game state changes).</summary>
        /// <returns>True when the arc ends where the grenade would go off.</returns>
        public static bool Fill(List<Vector3> points)
        {
            points.Clear();
            var grenade = _aiming != null ? AimedGrenade(_aiming) : null;
            return grenade != null && Predict(grenade, points);
        }

        private static GrenadeGear AimedGrenade(Player player)
        {
            var grenade = player.IsAlive && player == Player.LocalPlayer ? Grenade(player) : null;
            if (grenade == null || grenade.Active || ((IActivatedAbility)grenade).Charge < 1f || !Plugin.GrenadeAim.Value)
                return null;
            return grenade;
        }

        private static GrenadeGear Grenade(Player player)
        {
            var gear = player.Gear;
            return gear != null && gear.Length > 3 ? gear[3] as GrenadeGear : null;
        }

        private static bool Predict(GrenadeGear grenade, List<Vector3> points)
        {
            points.Clear();
            var look = PlayerLook.Instance;
            if (look == null)
                return false;
            var info = ((IUpgradable)grenade).Info;
            GunData gun = GunDataRef(grenade); // a copy: nothing on the grenade changes
            var fd = new FireData();
            float prepared = 0f;
            Vector3 firePos = PlayerLook.Position + PlayerLook.Rotation *
                FireOffset(info);
            gun.PrepareFireData(ref fd, firePos, look, ref prepared);
            BulletData data = gun.GetBulletData(ref fd.firePosition, ref fd.bulletRotation);
            grenade.ModifyBulletData(ref data, OwnerFlags);
            float lifetime = DefaultLifetime;
            Learned learned = null;
            _predictedLearned = info != null && LearnedData.TryGetValue(info, out learned);
            if (_predictedLearned)
            {
                var real = learned.Data;
                lifetime = learned.Lifetime;
                data.speed = real.speed;
                data.gravity = real.gravity;
                data.maxBounces = real.maxBounces;
                data.surfaceCollisionMask = real.surfaceCollisionMask;
                data.targetCollisionMask = real.targetCollisionMask;
                data.surfaceMagnetism = real.surfaceMagnetism;
                data.targetMagnetism = real.targetMagnetism;
                data.range = real.range;
                data.impactSize = real.impactSize;
            }
            if (data.surfaceCollisionMask == 0 && Player.LocalPlayer != null)
                data.surfaceCollisionMask = GroundMask(Player.LocalPlayer).value;
            _predicted = data;

            Vector3 velocity = data.direction * data.speed;
            Vector3 now = data.position, next = data.position;
            Quaternion rotNow = data.rotation, rotNext = data.rotation;
            var hit = new RaycastHit();
            int bounces = 0;
            points.Add(next);
            int steps = Mathf.Min(MaxSteps, Mathf.CeilToInt(lifetime / Time.fixedDeltaTime));
            float range = data.range.maxDamageRange;
            for (int i = 0; i < steps; i++)
            {
                int result = IBullet.SimulateProjectile(ref data, ref velocity, ref now, ref next, ref rotNow, ref rotNext, out hit, OwnerFlags);
                if (result == 0)
                {
                    points.Add(next);
                    if (range > 0f && (next - data.position).magnitude > range)
                    {
                        _lastEnd = "out of range after " + i + " steps, " + bounces + " bounces";
                        return true; // times out (detonates) here
                    }
                    continue;
                }
                points.Add(hit.point);
                if (bounces >= data.maxBounces)
                {
                    _lastEnd = "hit (" + result + ") at " + hit.point.ToString("F1") + " after " + i + " steps, " + bounces + " bounces";
                    return true; // detonates on this hit
                }
                bounces++;
                velocity = Vector3.Reflect(velocity, hit.normal) * 0.75f;
                data.direction = velocity.normalized;
                data.position = hit.point;
                now = next = hit.point;
            }
            _lastEnd = "no hit in " + steps + " steps, " + bounces + " bounces";
            return true; // its lifetime ends here: it times out (detonates) where the arc stops
        }
    }
}
