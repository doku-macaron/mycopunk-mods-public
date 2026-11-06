using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkHealthRegenTimer
{
    [BepInPlugin(Guid, "Mycopunk Health Regen Timer", "0.1.1")]
    [BepInDependency(MycopunkShared.MacaronCore.Guid)]
    [MycoMod(null, ModFlags.IsClientSide)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "f1w3.mycopunk.healthregentimer";
        private static readonly FieldInfo HealthBar = AccessTools.Field(typeof(Player), "healthbar");
        private static Plugin _instance;
        private Harmony _harmony;
        private ConfigEntry<bool> _enabled;
        private Player _player;
        private Graphic _bar;
        private TextMeshProUGUI _label;
        private int _sampleFrame = -1;
        private float _remaining;
        private bool _captureFaulted;

        private void Awake()
        {
            _instance = this;
            _enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Show the time until health regeneration begins above your health bar.",
                null, MenuText.Label("regen.enabled")));
            _harmony = new Harmony(Guid);
            ModsMenu.Install(_harmony, this);
            try
            {
                var method = AccessTools.Method(typeof(Player), "UpdateTarget");
                if (method == null || HealthBar == null) throw new MissingMemberException("Player.UpdateTarget or healthbar");
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(Plugin), nameof(CaptureTimer)));
            }
            catch (Exception e) { Logger.LogWarning("Health regeneration timer unavailable: " + e.Message); }
        }

        // Read before UpdateTarget resets the temporary regeneration speed for the next frame.
        private static void CaptureTimer(Player __instance, Player.UpgradeVariableData ___upgradeVariables,
            float ___noDamageTimeBeforeHealthRegenMultiplier, float ___tempMaxHealthCap,
            float ___forceHealthRegenEndTime, bool ___enableHealing)
        {
            if (_instance == null || _instance._captureFaulted || __instance != Player.LocalPlayer) return;
            _instance._sampleFrame = Time.frameCount;
            _instance._remaining = 0f;
            try
            {
                var network = NetworkManager.Singleton;
                if (network == null || !network.IsListening || !__instance.IsAlive || !___enableHealing ||
                    Time.timeScale <= 0f || __instance.NormalizedHealth >= ___tempMaxHealthCap ||
                    __instance.TempHealthRegenSpeed <= 0f) return;
                float now = network.ServerTime.TimeAsFloat;
                if (now < ___forceHealthRegenEndTime) return;
                float delay = ___upgradeVariables.noDamageTimeBeforeHealthRegen *
                    ___noDamageTimeBeforeHealthRegenMultiplier / __instance.TempHealthRegenSpeed;
                float remaining = delay - (now - __instance.LastDamageTime);
                if (!float.IsNaN(remaining) && !float.IsInfinity(remaining) && remaining > 0f)
                    _instance._remaining = remaining;
            }
            catch (Exception e)
            {
                _instance.Logger.LogWarning("Health regeneration timer could not read game state: " + e.Message);
                _instance._captureFaulted = true;
                _instance._sampleFrame = -1;
            }
        }

        private void LateUpdate()
        {
            var player = Player.LocalPlayer;
            if (player != _player)
            {
                if (_label != null) Destroy(_label.gameObject);
                _label = null;
                _bar = null;
                _player = player;
            }
            if (player == null || HealthBar == null) return;
            if (_bar == null) _bar = HealthBar.GetValue(player) as Graphic;
            if (_bar == null || _bar.rectTransform.parent == null || _bar.canvas == null)
            {
                if (_label != null) _label.gameObject.SetActive(false);
                return;
            }
            var canvas = _bar.canvas;
            var root = canvas.rootCanvas;
            bool visible = _enabled.Value && _sampleFrame == Time.frameCount && _remaining > 0f &&
                player.IsAlive && _bar.isActiveAndEnabled && canvas.isActiveAndEnabled && !HudVisibility.ResultsOpen();
            if (!visible)
            {
                if (_label != null) _label.gameObject.SetActive(false);
                return;
            }
            if (_label != null && _label.transform.parent != root.transform)
            {
                Destroy(_label.gameObject);
                _label = null;
            }
            if (_label == null)
            {
                var look = PlayerLook.Instance;
                var template = look != null ? look.PlayerTitleText : null;
                if (template == null) return;
                _label = Instantiate(template, root.transform, false);
                _label.name = "Health Regen Timer";
                for (int i = _label.transform.childCount - 1; i >= 0; i--)
                    Destroy(_label.transform.GetChild(i).gameObject);
                _label.raycastTarget = false;
                _label.richText = false;
                _label.enableAutoSizing = false;
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                _label.overflowMode = TextOverflowModes.Overflow;
                _label.alignment = TextAlignmentOptions.Bottom;
                _label.fontSize = 16f;
                _label.color = Color.white;
                var rect = _label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                _label.transform.SetAsLastSibling();
            }
            if (!_label.gameObject.activeSelf) _label.gameObject.SetActive(true);
            var bar = _bar.rectTransform;
            var scale = bar.localScale;
            var fullBar = Matrix4x4.TRS(bar.localPosition, bar.localRotation, new Vector3(1f, scale.y, scale.z));
            var world = bar.parent.TransformPoint(fullBar.MultiplyPoint3x4(new Vector3(bar.rect.center.x, bar.rect.yMax, 0f)));
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (root.worldCamera != null ? root.worldCamera : Camera.main);
            if (root.renderMode != RenderMode.ScreenSpaceOverlay && camera == null) { _label.gameObject.SetActive(false); return; }
            var screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            screen.y += 5f * Screen.height / 1080f;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.transform, screen, camera, out Vector2 local))
                _label.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
            _label.text = $"{_remaining:F1} s";
        }

        private void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
            if (_harmony != null) _harmony.UnpatchSelf();
            if (_instance == this) _instance = null;
        }
    }
}
