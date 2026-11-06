using System.Reflection;
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
    [BepInPlugin("f1w3.mycopunk.speedmonitor", "Mycopunk Speed Monitor", "0.1.0")]
    [BepInDependency(MycopunkShared.ModKit.Guid)]
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        private static readonly FieldInfo HealthBar = AccessTools.Field(typeof(Player), "healthbar");
        private Player _player;
        private Graphic _bar;
        private TextMeshProUGUI _label;
        private float _nextUpdate;
        private ConfigEntry<bool> _positionSpeed;
        private Vector3 _lastPosition;
        private float _lastSampleTime;
        private bool _hasSample;

        private void Awake()
        {
            _positionSpeed = Config.Bind("Display", "UsePositionSpeed", true, new ConfigDescription(
                "Calculate speed from world-position changes. Turn off to use the player's velocity instead.",
                null, MenuText.Label("speed.position")));
            ModsMenu.Install(new Harmony("f1w3.mycopunk.speedmonitor"), this);
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
            if (player == null || HealthBar == null) { _hasSample = false; return; }
            if (_bar == null) _bar = HealthBar.GetValue(player) as Graphic;
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
                _label = Instantiate(template, root.transform, false);
                _label.name = "Speed Monitor";
                for (int i = _label.transform.childCount - 1; i >= 0; i--)
                    Destroy(_label.transform.GetChild(i).gameObject);
                _label.raycastTarget = false;
                _label.richText = false;
                _label.enableAutoSizing = false;
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                _label.overflowMode = TextOverflowModes.Overflow;
                _label.alignment = TextAlignmentOptions.Top;
                _label.fontSize = 16f;
                _label.color = Color.white;
                var rect = _label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                _label.transform.SetAsLastSibling();
                Logger.LogInfo("Speed label created on " + root.name);
            }

            bool visible = player.IsAlive && _bar.isActiveAndEnabled && canvas.isActiveAndEnabled && !HudVisibility.ResultsOpen();
            if (_label.gameObject.activeSelf != visible) _label.gameObject.SetActive(visible);
            if (!visible) { _hasSample = false; return; }

            RectTransform bar = _bar.rectTransform;
            Vector3 scale = bar.localScale;
            var fullBar = Matrix4x4.TRS(bar.localPosition, bar.localRotation, new Vector3(1f, scale.y, scale.z));
            Vector3 world = bar.parent.TransformPoint(fullBar.MultiplyPoint3x4(new Vector3(bar.rect.center.x, bar.rect.yMin, 0f)));
            Camera camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (root.worldCamera != null ? root.worldCamera : Camera.main);
            if (root.renderMode != RenderMode.ScreenSpaceOverlay && camera == null) { _hasSample = false; return; }
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            screen.y -= 5f * Screen.height / 1080f;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.transform, screen, camera, out Vector2 local))
                _label.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
            if (Time.unscaledTime >= _nextUpdate)
            {
                float now = Time.unscaledTime;
                Vector3 position = player.transform.position;
                float elapsed = now - _lastSampleTime;
                float speed = _positionSpeed.Value
                    ? (_hasSample && elapsed > 0f ? Vector3.Distance(position, _lastPosition) / elapsed : 0f)
                    : player.Velocity.magnitude;
                _label.text = $"{speed:F1} m/s";
                _lastPosition = position;
                _lastSampleTime = now;
                _hasSample = true;
                _nextUpdate = now + 0.05f;
            }
        }

        private void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
        }
    }
}
