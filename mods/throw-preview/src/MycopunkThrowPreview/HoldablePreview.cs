using System.Collections.Generic;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace MycopunkThrowPreview
{
    /// <summary>
    /// Arc for a held object being charged for a throw (a teammate's part, barrels, rocks...).
    ///
    /// HoldableObject already throws on release: StartThrow stores the time, EndThrow sets the rigidbody's velocity
    /// to CalculateCurrentThrowVelocity() = PlayerLook.Forward * Lerp(minThrowForce, maxThrowForce, min(held / 0.6, 1)).
    /// From there it is plain rigidbody physics: gravity when useGravity, linear damping, and for DeadPlayerPart an
    /// extra (0, thrownGravity, 0) acceleration (its FixedUpdate, on the server). The arc steps that with the fixed
    /// time step until a sphere cast hits something that is not the object itself or the local player. The cast uses
    /// the local player's ground mask (Player.groundLayerMask): the held object's own layer is not a good guide,
    /// since while held it does not collide with the world.
    ///
    /// For a teammate's part it also tells whether the throw joins it to another part of the same player, which is
    /// what brings the teammate back (<see cref="Connects"/>; the arc then ends there and is drawn in the revive
    /// colour). The game joins two parts (DeadPlayerPart.HandleAutoConnect, every Update while held, flying or
    /// rolling) when the centres of their first sections, sections[initialActiveSection].parent's transform applied to
    /// its mainCollider.center, are less than 2 m apart, unless the part already has all its sections
    /// (AreAllSectionsActive) or its repairProgress is below 0. The centre is taken to keep its offset from the
    /// rigidbody along the arc (the part's turning in the air is not followed).
    /// </summary>
    internal static class HoldablePreview
    {
        private const int MaxSteps = 400;
        private const float Radius = 0.15f;

        private const float ConnectDistance = 2f; // HandleAutoConnect: sqrMagnitude < 4

        private static readonly List<Vector3> Targets = new List<Vector3>();

        /// <summary>True when the last arc joins a teammate's part to another of theirs (revives them).</summary>
        public static bool Connects { get; private set; }

        private static HoldableObject _charging;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        [HarmonyPatch(typeof(HoldableObject), nameof(HoldableObject.StartThrow))]
        internal static class StartThrowPatch
        {
            [HarmonyPostfix]
            private static void Postfix(HoldableObject __instance) => _charging = __instance;
        }

        /// <returns>True while an arc is to be drawn (filled into <paramref name="points"/>).</returns>
        public static bool Tick(List<Vector3> points)
        {
            var h = _charging;
            if (h == null)
                return false;
            // The object stops being Active when it leaves the hand (thrown or dropped).
            if (!h.Active || h.player != Player.LocalPlayer)
            {
                _charging = null;
                return false;
            }
            if (!Plugin.HoldableArc.Value || h.startThrowTime <= 0f)
                return false;
            var rb = h.rigidbody;
            if (rb == null)
                return false;
            Predict(h, rb, points);
            return true;
        }

        private static void Predict(HoldableObject h, Rigidbody rb, List<Vector3> points)
        {
            points.Clear();
            // Match the rendered body and camera, which can move after the last physics and Update steps.
            Vector3 p = rb.transform.position;
            Vector3 v = h.CalculateCurrentThrowVelocity();
            var look = PlayerLook.Instance;
            if (look != null)
                v = look.transform.forward * Vector3.Dot(v, PlayerLook.Forward);
            Vector3 a = rb.useGravity ? Physics.gravity : Vector3.zero;
            if (h is DeadPlayerPart part)
                a += new Vector3(0f, part.thrownGravity, 0f);
            float dt = Time.fixedDeltaTime;
            float damp = 1f / (1f + dt * rb.linearDamping);
            var local = Player.LocalPlayer;
            int mask = local != null ? local.groundLayerMask.value : Physics.DefaultRaycastLayers;
            if (mask == 0)
                mask = Physics.DefaultRaycastLayers;
            var me = local != null ? local.transform : null;

            // Where the part would join another one of the same teammate.
            Connects = false;
            Vector3 offset = default;
            bool joinable = h is DeadPlayerPart dp && JoinTargets(dp, p, out offset);

            points.Add(p);
            if (joinable && Joins(p, offset))
                return;
            for (int i = 0; i < MaxSteps; i++)
            {
                v = (v + a * dt) * damp;
                Vector3 step = v * dt;
                float len = step.magnitude;
                if (len > 1e-5f && Cast(p, step / len, len, mask, h.transform, me, out var hit))
                {
                    points.Add(hit);
                    if (joinable)
                        Joins(hit, offset);
                    return;
                }
                p += step;
                points.Add(p);
                if (joinable && Joins(p, offset))
                    return;
            }
        }

        private static bool Joins(Vector3 bodyPosition, Vector3 offset)
        {
            Vector3 centre = bodyPosition + offset;
            foreach (var t in Targets)
                if ((centre - t).sqrMagnitude < ConnectDistance * ConnectDistance)
                    return Connects = true;
            return false;
        }

        /// <summary>
        /// Fills <see cref="Targets"/> with the centres of the other parts of the same teammate, and gives the offset of
        /// this part's centre from its rigidbody. False when the game would not join this part now.
        /// </summary>
        private static bool JoinTargets(DeadPlayerPart part, Vector3 bodyPosition, out Vector3 offset)
        {
            offset = default;
            Targets.Clear();
            if (part.ParentPlayer == null || part.repairProgress < 0f)
                return false;
            if (part.AreAllSectionsActive)
                return false;
            var parts = DeadPlayerPart.allParts;
            if (parts == null || !parts.TryGetValue(part.ParentPlayer, out var list))
                return false;
            if (!Centre(part, out var mine))
                return false;
            offset = mine - bodyPosition;
            foreach (var other in list)
                if (other != null && other != part && Centre(other, out var c))
                    Targets.Add(c);
            return Targets.Count > 0;
        }

        /// <summary>The centre the game compares: sections[initialActiveSection].parent applied to mainCollider.center.</summary>
        private static bool Centre(DeadPlayerPart part, out Vector3 centre)
        {
            centre = default;
            var sections = part.sections;
            if (sections == null)
                return false;
            int i = part.initialActiveSection;
            if (i < 0 || i >= sections.Length)
                return false;
            var section = sections[i];
            var parent = section.parent;
            var box = section.mainCollider;
            if (parent == null || box == null)
                return false;
            centre = parent.transform.position + parent.transform.rotation * box.center;
            return true;
        }

        private static bool Cast(Vector3 from, Vector3 dir, float len, int mask, Transform self, Transform me, out Vector3 point)
        {
            int n = Physics.SphereCastNonAlloc(from, Radius, dir, Hits, len, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            point = default;
            for (int i = 0; i < n; i++)
            {
                var t = Hits[i].collider != null ? Hits[i].collider.transform : null;
                if (t == null || t.IsChildOf(self) || (me != null && t.IsChildOf(me)))
                    continue;
                if (Hits[i].distance <= 0f)
                    continue; // overlapping at the start of the step: not where it lands
                if (Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    point = Hits[i].point;
                }
            }
            return best < float.MaxValue;
        }
    }
}
