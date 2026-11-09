using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MycopunkHealthNumbers
{
    public enum AnchorMode
    {
        HealthBar,
        Screen,
    }

    public enum RendererMode
    {
        /// <summary>A copy of one of the game's TextMeshPro texts on the HUD canvas: the game's font and look.</summary>
        Game,
        /// <summary>Unity IMGUI on top of everything (the 0.1.0 look).</summary>
        OnGUI,
    }

    /// <summary>Current settings, read from the config entries (so changes in the cfg manager apply at once).</summary>
    internal sealed class Settings
    {
        internal ConfigEntry<bool> EnabledEntry;
        internal ConfigEntry<string> FormatEntry;
        internal ConfigEntry<int> DecimalsEntry;
        internal ConfigEntry<string> CapFormatEntry;
        internal ConfigEntry<bool> HideWhenDeadEntry;
        internal ConfigEntry<Color> ColorEntry;
        internal ConfigEntry<bool> TeammatesEntry;
        internal ConfigEntry<float> TeammateOffsetXEntry;
        internal ConfigEntry<float> TeammateOffsetYEntry;
        internal ConfigEntry<float> TeammateFontScaleEntry;
        internal ConfigEntry<bool> TeammateNameColorEntry;
        internal ConfigEntry<bool> BossEntry;

        public bool Enabled => EnabledEntry.Value;
        public string Format => FormatEntry.Value;
        public int Decimals => DecimalsEntry.Value;
        public string CapFormat => CapFormatEntry.Value;
        public bool HideWhenDead => HideWhenDeadEntry.Value;
        public Color Color => ColorEntry.Value;
        public bool Teammates => TeammatesEntry.Value;
        public float TeammateOffsetX => TeammateOffsetXEntry.Value;
        public float TeammateOffsetY => TeammateOffsetYEntry.Value;
        public float TeammateFontScale => TeammateFontScaleEntry.Value;
        public bool TeammateNameColor => TeammateNameColorEntry.Value;
        public bool Boss => BossEntry.Value;
    }

    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    // Client-side: it draws text and shares boss display data without changing gameplay.
    // Without this attribute the game treats any BepInEx plugin as a sandbox mod.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "t4t5u0.mycopunk.healthnumbers";
        public const string Name = "Mycopunk Health Numbers";
        public const string Version = "0.4.4";

        internal static ManualLogSource Log;
        internal static Settings Settings;
        internal static BossHealthNetwork BossNetwork;

        private ConfigEntry<AnchorMode> _anchor;
        private ConfigEntry<RendererMode> _renderer;
        private ConfigEntry<bool> _hideWithHealthBar;
        private ConfigEntry<float> _offsetX;
        private ConfigEntry<float> _offsetY;
        private ConfigEntry<float> _screenX;
        private ConfigEntry<float> _screenY;
        private ConfigEntry<int> _fontSize;
        private ConfigEntry<bool> _bold;
        private ConfigEntry<bool> _shadow;
        private ConfigEntry<Color> _shadowColor;
        private ConfigEntry<Key> _statDumpKey;
        private ConfigEntry<bool> _damageLog;

        private Harmony _harmony;
        private HealthBarLocator _locator;
        private readonly HealthText _localText = new HealthText();

        // Game renderer
        private TextMeshProUGUI _label;
        private Transform _labelParent;
        private bool _labelShadow;
        private Color _labelShadowColor;
        private bool _gameLabelFailed;

        // OnGUI renderer
        private GUIStyle _style;
        private GUIStyle _shadowStyle;
        private readonly GUIContent _content = new GUIContent();

        private void Awake()
        {
            Log = Logger;
            var s = new Settings();

            s.EnabledEntry = Config.Bind("General", "Enabled", true, new ConfigDescription("Show the health numbers.",
                null, MenuText.Label("health.enabled")));

            s.FormatEntry = Config.Bind("Display", "Format", "{0} / {1}",
                "Text to show. {0} is the current health, {1} is the max health (the capped max while capped).");
            s.DecimalsEntry = Config.Bind("Display", "Decimals", 1,
                new ConfigDescription("Digits after the decimal point. Values are rounded.",
                    new AcceptableValueRange<int>(0, 2)));
            s.CapFormatEntry = Config.Bind("Display", "CapFormat", " ({0})",
                "Appended while max health is temporarily capped (Mycologist's consumed max health, some objectives). " +
                "Format then shows the capped max as {1}, and {0} here is the original max health. " +
                "Leave empty to ignore the cap. Only for yourself: teammates' caps are not known on your machine.");
            s.HideWhenDeadEntry = Config.Bind("Display", "HideWhenDead", true, new ConfigDescription("Hide the text while the player is dead.",
                null, MenuText.Label("health.dead")));

            _anchor = Config.Bind("Position", "Anchor", AnchorMode.HealthBar,
                "HealthBar: place the text to the right of the game's health bar (falls back to Screen if the bar is not found). " +
                "Screen: place the text at ScreenX / ScreenY.");
            _hideWithHealthBar = Config.Bind("Position", "HideWithHealthBar", true, new ConfigDescription("With Anchor = HealthBar, hide the text while the game's health bar is hidden (menus, HUD off, and so on).",
                null, MenuText.Label("health.bar")));
            _offsetX = Config.Bind("Position", "OffsetX", 12f,
                "With Anchor = HealthBar: horizontal offset from the right end of the bar, in pixels at 1080p (scaled with the screen height).");
            _offsetY = Config.Bind("Position", "OffsetY", 0f,
                "With Anchor = HealthBar: vertical offset from the middle of the bar, in pixels at 1080p. Positive moves down.");
            _screenX = Config.Bind("Position", "ScreenX", 0.02f,
                new ConfigDescription("With Anchor = Screen (or as the fallback): left edge of the text, as a fraction of the screen width from the left.",
                    new AcceptableValueRange<float>(0f, 1f)));
            _screenY = Config.Bind("Position", "ScreenY", 0.93f,
                new ConfigDescription("With Anchor = Screen (or as the fallback): vertical middle of the text, as a fraction of the screen height from the top.",
                    new AcceptableValueRange<float>(0f, 1f)));

            _renderer = Config.Bind("Style", "Renderer", RendererMode.Game,
                "Game: draw with a copy of the game's own HUD text (same font, behind menus like the rest of the HUD). " +
                "OnGUI: draw with Unity IMGUI on top of everything (the look of 0.1.0). Game falls back to OnGUI if the HUD text is not found.");
            _fontSize = Config.Bind("Style", "FontSize", 22,
                new ConfigDescription("Font size in pixels at 1080p (scaled with the screen height).",
                    new AcceptableValueRange<int>(6, 200)));
            _bold = Config.Bind("Style", "Bold", true, new ConfigDescription("Use a bold font.",
                null, MenuText.Label("health.bold")));
            s.ColorEntry = Config.Bind("Style", "Color", Color.white, new ConfigDescription("Text color (RRGGBBAA).",
                null, MenuText.Label("health.color")));
            _shadow = Config.Bind("Style", "Shadow", true, new ConfigDescription("Draw a drop shadow behind the text so it stays readable.",
                null, MenuText.Label("health.shadow")));
            _shadowColor = Config.Bind("Style", "ShadowColor", new Color(0f, 0f, 0f, 0.8f), "Shadow color (RRGGBBAA).");

            s.TeammatesEntry = Config.Bind("Teammates", "Enabled", true, new ConfigDescription("Also show each teammate's health / max health on a line under their name in the teammate HUD. " +
                "Needs only this mod on your side; the game already sends teammates' health to everyone.",
                null, MenuText.Label("health.teammates")));
            s.TeammateOffsetXEntry = Config.Bind("Teammates", "OffsetX", 6f,
                "Not used since 0.4.0 (the teammate number is on its own line under the name now). Kept so old cfg files stay valid.");
            s.TeammateOffsetYEntry = Config.Bind("Teammates", "OffsetY", 0f,
                "Not used since 0.4.0 (the teammate number is on its own line under the name now). Kept so old cfg files stay valid.");
            s.TeammateFontScaleEntry = Config.Bind("Teammates", "FontScale", 0.9f,
                new ConfigDescription("Font size relative to the teammate's name.", new AcceptableValueRange<float>(0.3f, 3f)));
            s.TeammateNameColorEntry = Config.Bind("Teammates", "UseNameColor", true, new ConfigDescription("Use the color of the teammate's name. Off: use Style.Color.",
                null, MenuText.Label("health.name_color")));
            s.BossEntry = Config.Bind("Boss", "Enabled", true, new ConfigDescription(
                "Show boss health over the health bar. Composite bosses use an approximate bar-based value; clients can request the maximum from a host with this mod.",
                null, MenuText.Label("health.boss")));
            Settings = s;
            BossNetwork = new BossHealthNetwork(Log);

            _locator = new HealthBarLocator();

            _statDumpKey = Config.Bind("Debug", "StatDumpKey", Key.None,
                "Key that writes the Player prefab's base stats and your current stats to BepInEx/HealthNumbers/stats-*.md. None = off.");
            _damageLog = Config.Bind("Debug", "DamageLog", false, new ConfigDescription("Log every hit you take (raw damage, element, DoT, source, health lost, the one-hit guard) to the BepInEx log.",
                null, MenuText.Label("health.damage_log")));

            _harmony = new Harmony(Guid);
            ModsMenu.Install(_harmony, this);
            CapTracker.Install(_harmony);
            BossHealthLabel.Install(_harmony);
            DamageLog.Enabled = () => _damageLog.Value;
            DamageLog.Install(_harmony);
            try
            {
                TeammateHealthLabel.Install(_harmony);
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Teammate health numbers could not be hooked: " + e);
            }

            // StatDumpKey is a config entry, so the check finds it without a Register call.
            Hotkeys.ReportConflicts(this);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            BossNetwork?.Dispose();
            BossNetwork = null;
            if (_label != null) Destroy(_label.gameObject);
        }

        private void Update()
        {
            BossNetwork?.Tick();
            Key key = _statDumpKey.Value;
            Keyboard kb = Keyboard.current;
            if (key == Key.None || kb == null)
                return;
            try
            {
                if (kb[key].wasPressedThisFrame)
                    StatDump.Write();
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Stat dump failed: {e}");
            }
        }

        private bool UseGameRenderer => _renderer.Value == RendererMode.Game && !_gameLabelFailed;

        // ---------------- shared: where and what ----------------

        /// <summary>
        /// Where the text goes, in IMGUI screen coordinates (y down): x of the left edge and y of the middle.
        /// False when nothing should be shown.
        /// </summary>
        private bool TryGetPlacement(out Player player, out float x, out float yMid)
        {
            x = yMid = 0f;
            player = Player.LocalPlayer;
            if (!Settings.Enabled || player == null)
                return false;
            if (Settings.HideWhenDead && !player.IsAlive)
                return false;
            if (_anchor.Value == AnchorMode.HealthBar && _hideWithHealthBar.Value && HudVisibility.ResultsOpen())
                return false;

            float scale = Screen.height / 1080f;
            if (_anchor.Value == AnchorMode.HealthBar)
            {
                switch (_locator.TryGetScreenRect(player, out Rect bar))
                {
                    case HealthBarLocator.Result.Found:
                        x = bar.xMax + _offsetX.Value * scale;
                        yMid = bar.center.y + _offsetY.Value * scale;
                        return true;
                    case HealthBarLocator.Result.Hidden:
                        if (_hideWithHealthBar.Value)
                            return false;
                        break;
                }
            }
            x = _screenX.Value * Screen.width;
            yMid = _screenY.Value * Screen.height;
            return true;
        }

        private string LocalText(Player player)
            => _localText.Get(player.Health, player.MaxHealth, CapTracker.Current, Settings.Decimals, Settings.Format, Settings.CapFormat);

        // ---------------- Game renderer ----------------

        private void LateUpdate()
        {
            if (!UseGameRenderer)
            {
                if (_label != null)
                    GameHudText.SetActive(_label, false);
                return;
            }
            try
            {
                if (!TryGetPlacement(out Player player, out float x, out float yMid) || !EnsureLabel(out Canvas root))
                {
                    if (_label != null)
                        GameHudText.SetActive(_label, false);
                    return;
                }

                GameHudText.SetText(_label, LocalText(player));
                float size = _fontSize.Value * (Screen.height / 1080f) / Mathf.Max(0.0001f, root.scaleFactor);
                if (_label.fontSize != size)
                    _label.fontSize = size;
                var style = _bold.Value ? FontStyles.Bold : FontStyles.Normal;
                if (_label.fontStyle != style)
                    _label.fontStyle = style;
                if (_label.color != TextColor)
                    _label.color = TextColor;
                if (_labelShadow != _shadow.Value || _labelShadowColor != _shadowColor.Value)
                {
                    _labelShadow = _shadow.Value;
                    _labelShadowColor = _shadowColor.Value;
                    GameText.SetShadow(_label, _labelShadow, _labelShadowColor);
                }

                Camera cam = HudCoordinates.CameraFor(root);
                var screenPoint = new Vector2(x, Screen.height - yMid);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_labelParent, screenPoint, cam, out Vector2 local))
                    _label.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
                GameHudText.SetActive(_label, true);
            }
            catch (System.Exception e)
            {
                _gameLabelFailed = true;
                if (_label != null)
                    Destroy(_label.gameObject);
                _label = null;
                Log.LogWarning("Game-style health label failed; using OnGUI instead: " + e);
            }
        }

        private static Color TextColor => Settings.Color;

        /// <summary>Creates the label using the game's player title text.</summary>
        private bool EnsureLabel(out Canvas root)
        {
            root = null;
            var look = PlayerLook.Instance;
            TextMeshProUGUI template = look != null ? look.PlayerTitleText : null;
            if (template == null)
                return false;
            Canvas canvas = template.canvas;
            if (canvas == null)
                return false;
            root = canvas.rootCanvas;
            Transform parent = root.transform;
            if (_label != null && _labelParent == parent)
                return true;

            if (_label != null)
                Destroy(_label.gameObject);
            _labelParent = parent;
            _label = GameText.Create(template, _labelParent, "Health Numbers (local)");
            _label.transform.SetAsLastSibling();
            _labelShadow = !_shadow.Value; // force the shadow to be applied on the first frame
            Log.LogInfo($"Health label: copied {template.name} onto canvas {root.name} ({root.renderMode})");
            return true;
        }

        // ---------------- OnGUI renderer ----------------

        private void OnGUI()
        {
            if (UseGameRenderer || Event.current.type != EventType.Repaint)
                return;
            if (!TryGetPlacement(out Player player, out float x, out float yMid))
                return;

            float scale = Screen.height / 1080f;
            EnsureStyles();
            int fontSize = Mathf.Max(1, Mathf.RoundToInt(_fontSize.Value * scale));
            _style.fontSize = fontSize;
            _style.fontStyle = _bold.Value ? FontStyle.Bold : FontStyle.Normal;
            _style.normal.textColor = TextColor;

            _content.text = LocalText(player);
            Vector2 size = _style.CalcSize(_content);
            var rect = new Rect(Mathf.Round(x), Mathf.Round(yMid - size.y / 2f), size.x, size.y);

            if (_shadow.Value)
            {
                _shadowStyle.fontSize = fontSize;
                _shadowStyle.fontStyle = _style.fontStyle;
                _shadowStyle.normal.textColor = _shadowColor.Value;
                float d = Mathf.Max(1f, Mathf.Round(2f * scale));
                GUI.Label(new Rect(rect.x + d, rect.y + d, rect.width, rect.height), _content, _shadowStyle);
            }
            GUI.Label(rect, _content, _style);
        }

        private void EnsureStyles()
        {
            if (_style != null)
                return;
            // GUI.skin is only usable inside OnGUI, so the styles are made lazily here.
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _shadowStyle = new GUIStyle(_style);
        }
    }

    /// <summary>
    /// Finds where the local player's health bar is on screen.
    /// The game shrinks the bar with localScale.x (weighted health), so the full-width rect is computed
    /// as if localScale.x were 1.
    /// </summary>
    internal sealed class HealthBarLocator
    {
        public enum Result
        {
            NotFound,
            Hidden,
            Found,
        }

        private readonly Vector3[] _corners = new Vector3[4];

        private Player _player;
        private RectTransform _bar;
        private Canvas _canvas;
        private bool _found;

        public Result TryGetScreenRect(Player player, out Rect screenRect)
        {
            screenRect = default;

            // Look the bar up once per player; look again only if a bar we found has been destroyed.
            bool lostBar = _found && (_bar == null || _canvas == null);
            if (!ReferenceEquals(player, _player) || lostBar)
            {
                _found = false;
                _player = player;
                _bar = null;
                _canvas = null;
                if (player.healthbar is Component c && c != null)
                {
                    _bar = c.transform as RectTransform;
                    if (_bar != null)
                        _canvas = _bar.GetComponentInParent<Canvas>(true);
                }
                _found = _canvas != null;
                if (_found)
                    Plugin.Log.LogInfo($"Health bar: {_bar.name}, canvas {_canvas.rootCanvas.name} ({_canvas.rootCanvas.renderMode}), rect {_bar.rect}");
                else
                    Plugin.Log.LogInfo("Health bar not found for the local player; using the Screen position.");
            }
            if (_bar == null || _canvas == null)
                return Result.NotFound;

            if (!_bar.gameObject.activeInHierarchy || !_canvas.isActiveAndEnabled)
                return Result.Hidden;

            Canvas root = _canvas.rootCanvas;
            Camera cam = null;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = HudCoordinates.CameraFor(root);
                if (cam == null)
                    return Result.NotFound;
            }

            Transform parent = _bar.parent;
            Vector3 s = _bar.localScale;
            Matrix4x4 local = Matrix4x4.TRS(_bar.localPosition, _bar.localRotation, new Vector3(1f, s.y, s.z));
            Matrix4x4 toWorld = parent != null ? parent.localToWorldMatrix * local : local;

            Rect r = _bar.rect;
            _corners[0] = toWorld.MultiplyPoint3x4(new Vector3(r.xMin, r.yMin, 0f));
            _corners[1] = toWorld.MultiplyPoint3x4(new Vector3(r.xMin, r.yMax, 0f));
            _corners[2] = toWorld.MultiplyPoint3x4(new Vector3(r.xMax, r.yMax, 0f));
            _corners[3] = toWorld.MultiplyPoint3x4(new Vector3(r.xMax, r.yMin, 0f));

            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = cam == null ? _corners[i] : cam.WorldToScreenPoint(_corners[i]);
                if (cam != null && p.z < 0f)
                    return Result.Hidden;
                xMin = Mathf.Min(xMin, p.x);
                xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y);
                yMax = Mathf.Max(yMax, p.y);
            }

            // Screen space has y up; IMGUI has y down.
            screenRect = Rect.MinMaxRect(xMin, Screen.height - yMax, xMax, Screen.height - yMin);
            return Result.Found;
        }
    }
}
