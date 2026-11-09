using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkSpeedMonitor
{
    [BepInPlugin("f1w3.mycopunk.speedmonitor", "Mycopunk Speed Monitor", "0.1.1")]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        private Player _player;
        private Graphic _bar;
        private TextMeshProUGUI _label;
        private double _nextUpdate;
        private ConfigEntry<bool> _positionSpeed;
        private ConfigEntry<bool> _includeVertical;
        private Vector3 _lastPosition;
        private double _lastSampleTime;
        private bool _hasSample;
        private bool _hasSmoothedSpeed;
        private bool _lastPositionSpeed;
        private bool _lastIncludeVertical;
        private float _smoothedSpeed;
        private readonly List<(double time, Vector3 position)> _positions = new List<(double, Vector3)>();

        private void Awake()
        {
            _positionSpeed = Config.Bind("Display", "UsePositionSpeed", true, new ConfigDescription(
                "Calculate speed from world-position changes. Turn off to use the player's velocity instead.",
                null, MenuText.Label("speed.position")));
            _includeVertical = Config.Bind("Display", "IncludeVerticalSpeed", true, new ConfigDescription(
                "Include vertical movement in speed. Turn off to show horizontal speed only.",
                null, MenuText.Label("speed.vertical")));
            _harmony = new Harmony("f1w3.mycopunk.speedmonitor");
            ModsMenu.Install(_harmony, this);
        }

        private void LateUpdate()
        {
            Player player = Player.LocalPlayer;
            if (player != _player)
            {
                if (_label != null) Destroy(_label.gameObject);
                _label = null;
                _bar = null;
                _player = player;
                _hasSample = false;
                _nextUpdate = 0f;
            }
            if (player == null) { _hasSample = false; return; }
            if (_bar == null) _bar = player.healthbar;
            if (_bar == null || _bar.rectTransform.parent == null) { _hasSample = false; return; }
            Canvas canvas = _bar.canvas;
            if (canvas == null) { _hasSample = false; return; }
            Canvas root = canvas.rootCanvas;

            if (_label != null && _label.transform.parent != root.transform)
            {
                Destroy(_label.gameObject);
                _label = null;
            }
            if (_label == null)
            {
                var look = PlayerLook.Instance;
                var template = look != null ? look.PlayerTitleText : null;
                if (template == null) { _hasSample = false; return; }
                _label = GameHudText.Create(template, root.transform, "Speed Monitor");
                _label.alignment = TextAlignmentOptions.Top;
                _label.fontSize = 16f;
                _label.color = Color.white;
                // Placed under the health bar every frame (below). A movable position is planned with HudPlacement.
                var rect = _label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                _label.transform.SetAsLastSibling();
                Logger.LogInfo("Speed label created on " + root.name);
            }

            bool visible = HudVisibility.HealthBarVisible(player, _bar, canvas);
            GameHudText.SetActive(_label, visible);
            if (!visible) { _hasSample = false; return; }

            // Under the middle of the health bar's full width (its scale.x is the current health), 5 px at 1080p.
            RectTransform bar = _bar.rectTransform;
            Vector3 scale = bar.localScale;
            var fullBar = Matrix4x4.TRS(bar.localPosition, bar.localRotation, new Vector3(1f, scale.y, scale.z));
            Vector3 world = bar.parent.TransformPoint(fullBar.MultiplyPoint3x4(new Vector3(bar.rect.center.x, bar.rect.yMin, 0f)));
            Camera camera = HudCoordinates.CameraFor(root);
            if (root.renderMode != RenderMode.ScreenSpaceOverlay && camera == null) { _hasSample = false; return; }
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            screen.y -= 5f * Screen.height / 1080f;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.transform, screen, camera, out Vector2 local))
                _label.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);

            if (Time.unscaledTimeAsDouble >= _nextUpdate)
            {
                double now = Time.unscaledTimeAsDouble;
                Vector3 position = player.transform.position;
                float elapsed = (float)(now - _lastSampleTime);
                bool positionSpeed = _positionSpeed.Value;
                bool includeVertical = _includeVertical.Value;
                if (!includeVertical) position.y = 0f;
                if (!_hasSample || positionSpeed != _lastPositionSpeed || includeVertical != _lastIncludeVertical)
                {
                    _hasSample = false;
                    _hasSmoothedSpeed = false;
                    _positions.Clear();
                }
                float instantSpeed = _hasSample && elapsed > 0f ? Vector3.Distance(position, _lastPosition) / elapsed : 0f;
                float speed;
                if (positionSpeed)
                    speed = PositionSpeed(now, position, _hasSample && instantSpeed < 0.01f);
                else
                {
                    Vector3 velocity = player.Velocity;
                    if (!includeVertical) velocity.y = 0f;
                    speed = velocity.magnitude;
                }
                if (!positionSpeed || (_hasSample && elapsed > 0f))
                {
                    if (!_hasSmoothedSpeed || speed < 0.01f)
                        _smoothedSpeed = speed;
                    else
                        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-elapsed / 0.15f));
                    _hasSmoothedSpeed = true;
                    speed = _smoothedSpeed;
                }
                GameHudText.SetText(_label, $"{speed:F1} m/s");
                _lastPosition = position;
                _lastSampleTime = now;
                _hasSample = true;
                _lastPositionSpeed = positionSpeed;
                _lastIncludeVertical = includeVertical;
                _nextUpdate = now + 0.05f;
            }
        }

        private float PositionSpeed(double now, Vector3 position, bool stopped)
        {
            if (stopped) _positions.Clear();
            _positions.Add((now, position));
            double start = now - 0.5;
            while (_positions.Count > 1 && _positions[1].time <= start) _positions.RemoveAt(0);
            if (_positions.Count < 2) return 0f;

            var first = _positions[0];
            var second = _positions[1];
            double from = System.Math.Max(start, first.time);
            float fraction = (float)((from - first.time) / (second.time - first.time));
            Vector3 previous = Vector3.Lerp(first.position, second.position, fraction);
            float distance = 0f;
            for (int i = 1; i < _positions.Count; i++)
            {
                distance += Vector3.Distance(previous, _positions[i].position);
                previous = _positions[i].position;
            }
            double duration = now - from;
            return duration > 0 ? distance / (float)duration : 0f;
        }

        private void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
            _harmony?.UnpatchSelf();
        }
    }
}
