using System;
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
    [BepInPlugin(Guid, "Mycopunk Health Regen Timer", "0.1.2")]
    [BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
    [MycoMod(null, ModFlags.IsClientSide)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "f1w3.mycopunk.healthregentimer";
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
                _harmony.CreateClassProcessor(typeof(UpdateTargetPatch)).Patch();
            }
            catch (Exception e) { Logger.LogWarning("Health regeneration timer unavailable: " + e.Message); }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateTarget))]
        private static class UpdateTargetPatch
        {
            // Read before UpdateTarget resets the temporary regeneration speed for the next frame.
            [HarmonyPrefix]
            private static void Prefix(Player __instance) => CaptureTimer(__instance);
        }

        private static void CaptureTimer(Player player)
        {
            if (_instance == null || _instance._captureFaulted || player != Player.LocalPlayer) return;
            _instance._sampleFrame = Time.frameCount;
            _instance._remaining = 0f;
            try
            {
                var network = NetworkManager.Singleton;
                if (network == null || !network.IsListening || !player.IsAlive || !player.enableHealing ||
                    Time.timeScale <= 0f || player.NormalizedHealth >= player.tempMaxHealthCap ||
                    player.TempHealthRegenSpeed <= 0f) return;
                float now = network.ServerTime.TimeAsFloat;
                if (now < player.forceHealthRegenEndTime) return;
                float delay = player.upgradeVariables.noDamageTimeBeforeHealthRegen *
                    player.noDamageTimeBeforeHealthRegenMultiplier / player.TempHealthRegenSpeed;
                float remaining = delay - (now - player.LastDamageTime);
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
            if (player == null) return;
            if (_bar == null) _bar = player.healthbar;
            if (_bar == null || _bar.rectTransform.parent == null || _bar.canvas == null)
            {
                if (_label != null) _label.gameObject.SetActive(false);
                return;
            }
            var canvas = _bar.canvas;
            var root = canvas.rootCanvas;
            bool visible = _enabled.Value && _sampleFrame == Time.frameCount && _remaining > 0f &&
                HudVisibility.HealthBarVisible(player, _bar, canvas);
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
                _label = GameHudText.Create(template, root.transform, "Health Regen Timer");
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
            var camera = HudCoordinates.CameraFor(root);
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
