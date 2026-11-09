using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MycopunkThrowPreview
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // Client-side and not a sandbox mod: it draws the local trajectory and only delays the local player's own throw.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.throwpreview";
        public const string Name = "Mycopunk Throw Preview";
        public const string Version = "0.3.0";

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
        private Harmony _harmony;

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
                "Arc color (RRGGBBAA).", null, MenuText.Label("throw.color")));
            ReviveColor = Config.Bind("Style", "ReviveColor", new Color(0.3f, 1f, 0.4f, 0.9f), new ConfigDescription(
                "Arc color when a thrown teammate's part would join another of their parts (which revives them). RRGGBBAA.",
                null, MenuText.Label("throw.revive_color")));
            Thickness = Config.Bind("Style", "Thickness", 3f, "Arc thickness (1 unit = 0.01 meters).");
            DashLength = Config.Bind("Style", "DashLength", 12f, "Dash length (1 unit = 0.05 meters).");
            GapLength = Config.Bind("Style", "GapLength", 8f, "Gap length (1 unit = 0.05 meters; 0 = solid).");

            // Each part on its own, so one failing (game update) does not take the others down.
            _harmony = new Harmony(Guid);
            ModSetup.Try(Log, "grenade fire points", () => MycopunkThrowPreview.GrenadeAim.LoadOffsets(
                System.IO.Path.Combine(Paths.BepInExRootPath, "ThrowPreview", "fire-offsets.tsv")));
            ModSetup.Try(Log, "grenade aiming", () => MycopunkThrowPreview.GrenadeAim.Install(_harmony));
            ModSetup.Try(Log, "held object arcs", () => _harmony.CreateClassProcessor(typeof(HoldablePreview.StartThrowPatch)).Patch());
            ModSetup.Try(Log, "MODS tab", () => ModsMenu.Install(_harmony, this));
            ModSetup.Try(Log, "hotkey report", () => Hotkeys.ReportConflicts(this));
            Log.LogInfo(Name + " " + Version + " loaded (grenade aim=" + GrenadeAim.Value + ", object arcs=" + HoldableArc.Value + ").");
        }

        private void OnEnable()
        {
            Camera.onPreCull += Redraw;
            RenderPipelineManager.beginCameraRendering += RedrawSrp;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= Redraw;
            RenderPipelineManager.beginCameraRendering -= RedrawSrp;
            _drawer.Hide();
        }

        private void OnDestroy()
        {
            MycopunkThrowPreview.GrenadeAim.Disable();
            _harmony?.UnpatchSelf();
            _drawer.Destroy();
        }

        /// <summary>
        /// Throws on release; game state changes stay in the normal update.
        /// </summary>
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
                MycopunkThrowPreview.GrenadeAim.Disable();
                _drawer.Hide();
                Log.LogError("Throw preview failed and is turned off: " + e);
            }
        }

        private void RedrawSrp(ScriptableRenderContext context, Camera camera) => Redraw(camera);

        private void Redraw(Camera camera)
        {
            if (_failed || camera != PlayerLook.LocalCamera)
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
                MycopunkThrowPreview.GrenadeAim.Disable();
                _drawer.Hide();
                Log.LogError("Throw preview failed and is turned off: " + e);
            }
        }

    }
}
