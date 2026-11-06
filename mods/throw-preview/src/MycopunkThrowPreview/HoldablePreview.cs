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

        private static readonly AccessTools.FieldRef<HoldableObject, float> StartThrowTime =
            AccessTools.FieldRefAccess<HoldableObject, float>("startThrowTime");
        private static readonly AccessTools.FieldRef<HoldableObject, Rigidbody> Body =
            AccessTools.FieldRefAccess<HoldableObject, Rigidbody>("rigidbody");
        private static readonly AccessTools.FieldRef<HoldableObject, Player> Holder =
            AccessTools.FieldRefAccess<HoldableObject, Player>("player");
        private static readonly AccessTools.FieldRef<Player, LayerMask> GroundMask =
            AccessTools.FieldRefAccess<Player, LayerMask>("groundLayerMask");
        private static readonly AccessTools.FieldRef<DeadPlayerPart, float> ThrownGravity =
            AccessTools.FieldRefAccess<DeadPlayerPart, float>("thrownGravity");

        private const float ConnectDistance = 2f; // HandleAutoConnect: sqrMagnitude < 4

        private static readonly AccessTools.FieldRef<DeadPlayerPart, float> RepairProgress =
            AccessTools.FieldRefAccess<DeadPlayerPart, float>("repairProgress");
        private static readonly AccessTools.FieldRef<DeadPlayerPart, int> InitialSection =
            AccessTools.FieldRefAccess<DeadPlayerPart, int>("initialActiveSection");
        private static readonly System.Reflection.FieldInfo Sections = AccessTools.Field(typeof(DeadPlayerPart), "sections");
        private static readonly System.Reflection.FieldInfo AllParts = AccessTools.Field(typeof(DeadPlayerPart), "allParts");
        private static readonly System.Reflection.PropertyInfo AllSectionsActive = AccessTools.Property(typeof(DeadPlayerPart), "AreAllSectionsActive");
        private static System.Reflection.FieldInfo _sectionParent, _sectionCollider;
        private static readonly List<Vector3> Targets = new List<Vector3>();

        /// <summary>True when the last arc joins a teammate's part to another of theirs (revives them).</summary>
        public static bool Connects { get; private set; }

        private static HoldableObject _charging;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static void Install(Harmony harmony)
        {
            var start = AccessTools.Method(typeof(HoldableObject), "StartThrow");
            if (start == null)
            {
                Plugin.Log.LogWarning("HoldableObject.StartThrow not found; arcs for held objects are off.");
                return;
            }
            harmony.Patch(start, postfix: new HarmonyMethod(typeof(HoldablePreview), nameof(StartThrow_Postfix)));
        }

        private static void StartThrow_Postfix(HoldableObject __instance) => _charging = __instance;

        /// <returns>True while an arc is to be drawn (filled into <paramref name="points"/>).</returns>
        public static bool Tick(List<Vector3> points)
        {
            var h = _charging;
            if (h == null)
                return false;
            // The object stops being Active when it leaves the hand (thrown or dropped).
            if (!h.Active || Holder(h) != Player.LocalPlayer)
            {
                _charging = null;
                return false;
            }
            if (!Plugin.HoldableArc.Value || StartThrowTime(h) <= 0f)
                return false;
            var rb = Body(h);
            if (rb == null)
                return false;
            Predict(h, rb, points);
            return true;
        }

        private static void Predict(HoldableObject h, Rigidbody rb, List<Vector3> points)
        {
            points.Clear();
            Vector3 p = rb.position;
            Vector3 v = h.CalculateCurrentThrowVelocity();
            Vector3 a = rb.useGravity ? Physics.gravity : Vector3.zero;
            if (h is DeadPlayerPart part)
                a += new Vector3(0f, ThrownGravity(part), 0f);
            float dt = Time.fixedDeltaTime;
            float damp = 1f / (1f + dt * rb.linearDamping);
            var local = Player.LocalPlayer;
            int mask = local != null ? GroundMask(local).value : Physics.DefaultRaycastLayers;
            if (mask == 0)
                mask = Physics.DefaultRaycastLayers;
            var me = local != null ? local.transform : null;

            // Where the part would join another one of the same teammate.
            Connects = false;
            Vector3 offset = default;
            bool joinable = h is DeadPlayerPart dp && JoinTargets(dp, rb, out offset);

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
        private static bool JoinTargets(DeadPlayerPart part, Rigidbody rb, out Vector3 offset)
        {
            offset = default;
            Targets.Clear();
            if (Sections == null || AllParts == null || part.ParentPlayer == null || RepairProgress(part) < 0f)
                return false;
            if (AllSectionsActive != null && AllSectionsActive.GetValue(part) is bool all && all)
                return false;
            if (!(AllParts.GetValue(null) is Dictionary<Player, List<DeadPlayerPart>> parts) ||
                !parts.TryGetValue(part.ParentPlayer, out var list))
                return false;
            if (!Centre(part, out var mine))
                return false;
            offset = mine - rb.position;
            foreach (var other in list)
                if (other != null && other != part && Centre(other, out var c))
                    Targets.Add(c);
            return Targets.Count > 0;
        }

        /// <summary>The centre the game compares: sections[initialActiveSection].parent applied to mainCollider.center.</summary>
        private static bool Centre(DeadPlayerPart part, out Vector3 centre)
        {
            centre = default;
            if (!(Sections.GetValue(part) is System.Array sections))
                return false;
            int i = InitialSection(part);
            if (i < 0 || i >= sections.Length)
                return false;
            object section = sections.GetValue(i);
            if (_sectionParent == null)
            {
                _sectionParent = AccessTools.Field(section.GetType(), "parent");
                _sectionCollider = AccessTools.Field(section.GetType(), "mainCollider");
            }
            var parent = _sectionParent?.GetValue(section) as GameObject;
            var box = _sectionCollider?.GetValue(section) as BoxCollider;
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
