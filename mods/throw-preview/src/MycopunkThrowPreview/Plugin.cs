using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;
using UnityEngine;

namespace MycopunkThrowPreview
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.ModKit.Guid)]
    // Client-side and not a sandbox mod: it draws on the local HUD and only delays the local player's own throw.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.throwpreview";
        public const string Name = "Mycopunk Throw Preview";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> GrenadeAim;
        internal static ConfigEntry<bool> HoldableArc;
        internal static ConfigEntry<Color> ArcColor;
        internal static ConfigEntry<Color> ReviveColor;
        internal static ConfigEntry<float> Thickness;
        internal static ConfigEntry<float> DashLength;
        internal static ConfigEntry<float> GapLength;

        private readonly ArcDrawer _drawer = new ArcDrawer();
        private readonly List<Vector3> _points = new List<Vector3>();
        private bool _failed;

        private void Awake()
        {
            Log = Logger;
            GrenadeAim = Config.Bind("Grenade", "HoldToAim", true, new ConfigDescription(
                "Hold the grenade key to see where it will go, release to throw (the game throws on press). " +
                "A quick tap still throws at once. Takes effect at once.",
                null, MenuText.Label("throw.grenade")));
            HoldableArc = Config.Bind("Holdables", "ShowArc", true, new ConfigDescription(
                "Show the arc while charging a throw of a held object (a teammate's part, barrels...). Takes effect at once.",
                null, MenuText.Label("throw.arc")));
            ArcColor = Config.Bind("Style", "Color", new Color(1f, 1f, 1f, 0.75f), new ConfigDescription(
                "Arc colour (RRGGBBAA).", null, MenuText.Label("throw.color")));
            ReviveColor = Config.Bind("Style", "ReviveColor", new Color(0.3f, 1f, 0.4f, 0.9f), new ConfigDescription(
                "Arc colour when a thrown teammate's part would join another of their parts (which revives them). RRGGBBAA.",
                null, MenuText.Label("throw.revive_color")));
            Thickness = Config.Bind("Style", "Thickness", 3f, "Arc thickness, in HUD units.");
            DashLength = Config.Bind("Style", "DashLength", 12f, "Length of each dash of the arc, in HUD units.");
            GapLength = Config.Bind("Style", "GapLength", 8f, "Gap between dashes, in HUD units (0 = a solid line).");

            // Each part on its own, so one failing (game update) does not take the others down.
            var harmony = new Harmony(Guid);
            Try("grenade fire points", () => MycopunkThrowPreview.GrenadeAim.LoadOffsets(
                System.IO.Path.Combine(Paths.BepInExRootPath, "ThrowPreview", "fire-offsets.tsv")));
            Try("grenade aiming", () => MycopunkThrowPreview.GrenadeAim.Install(harmony));
            Try("held object arcs", () => HoldablePreview.Install(harmony));
            Try("MODS tab", () => ModsMenu.Install(harmony, this));
            Try("hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (grenade aim=" + GrenadeAim.Value + ", object arcs=" + HoldableArc.Value + ").");
        }

        private void OnEnable() => Canvas.willRenderCanvases += Redraw;

        private void OnDisable() => Canvas.willRenderCanvases -= Redraw;

        private void OnDestroy() => _drawer.Destroy();

        /// <summary>
        /// Runs right before the UI is drawn, after every LateUpdate, so the camera and the player have moved for this
        /// frame. Drawing in LateUpdate projected the arc with the camera of before its move, which left a trail
        /// when jumping.
        /// </summary>
        /// <summary>Throws on release (game state changes stay in the normal update, not in the UI callback).</summary>
        private void Update()
        {
            if (_failed)
                return;
            try
            {
                MycopunkThrowPreview.GrenadeAim.Tick();
            }
            catch (System.Exception e)
            {
                _failed = true;
                _drawer.Hide();
                Log.LogError("Throw preview failed and is turned off: " + e);
            }
        }

        private void Redraw()
        {
            if (_failed)
                return;
            try
            {
                bool hasEnd = MycopunkThrowPreview.GrenadeAim.Fill(_points);
                bool drawing = MycopunkThrowPreview.GrenadeAim.Aiming;
                bool revive = false;
                if (!drawing)
                {
                    drawing = HoldablePreview.Tick(_points);
                    hasEnd = drawing;
                    revive = drawing && HoldablePreview.Connects;
                }
                if (drawing)
                    _drawer.Draw(_points, hasEnd, revive ? ReviveColor.Value : ArcColor.Value, Thickness.Value, DashLength.Value, GapLength.Value);
                else
                    _drawer.Hide();
            }
            catch (System.Exception e)
            {
                _failed = true;
                _drawer.Hide();
                Log.LogError("Throw preview failed and is turned off: " + e);
            }
        }

        private static void Try(string what, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                Log.LogError("Could not set up " + what + "; it is off: " + e);
            }
        }
    }
}
