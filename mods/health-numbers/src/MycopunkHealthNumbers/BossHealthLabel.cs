using System;
using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkHealthNumbers
{
    internal sealed class BossHealthLabel : MonoBehaviour
    {
        private readonly HealthText _text = new HealthText();
        private TextMeshProUGUI _label;
        private RectTransform _parent;
        private string _lastApproxSource;
        private string _lastApproxText;
        private bool _failed;

        internal static void Install(Harmony harmony)
        {
            Install(harmony, typeof(BossBarPatch), typeof(BossHealthbar));
            Install(harmony, typeof(RhomboBarPatch), typeof(RhombominationHealthbar));
        }

        private static void Install(Harmony harmony, Type patch, Type type)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Boss health unavailable for " + type.Name + ": " + e.Message); }
        }

        [HarmonyPatch(typeof(BossHealthbar), nameof(BossHealthbar.Update))]
        private static class BossBarPatch
        {
            [HarmonyPostfix]
            private static void Postfix(BossHealthbar __instance) => Attach(__instance);
        }

        [HarmonyPatch(typeof(RhombominationHealthbar), nameof(RhombominationHealthbar.Update))]
        private static class RhomboBarPatch
        {
            [HarmonyPostfix]
            private static void Postfix(RhombominationHealthbar __instance) => Attach(__instance);
        }

        private static void Attach(MonoBehaviour __instance)
        {
            if (__instance != null && __instance.GetComponent<BossHealthLabel>() == null)
                __instance.gameObject.AddComponent<BossHealthLabel>();
        }

        private void LateUpdate()
        {
            if (_failed) return;
            try { Refresh(); }
            catch (Exception e)
            {
                _failed = true;
                if (_label != null) _label.gameObject.SetActive(false);
                Plugin.Log.LogWarning("Boss health label disabled: " + e.Message);
            }
        }

        private void Refresh()
        {
            var settings = Plugin.Settings;
            if (settings == null || !settings.Enabled || !settings.Boss || HudVisibility.ResultsOpen()) { Hide(); return; }
            Graphic bar = null;
            TextMeshProUGUI template = null;
            float health = 0f, max = 0f;
            bool approximate = false, percent = false;
            var boss = GetComponent<BossHealthbar>();
            if (boss != null)
            {
                var brain = boss.target;
                if (brain == null) { Hide(); return; }
                bar = boss.healthbar;
                template = boss.nameText;
                if (boss is AmalgamationHealthbar amalgamation)
                {
                    bool killable = amalgamation.canBeKilled;
                    if (killable) bar = amalgamation.totalHealthbar;
                    if (!killable)
                    {
                        var behavior = brain.GetComponent<AmalgamationBehaviour>();
                        if (behavior == null) { Hide(); return; }
                        var globalEvent = Global.Instance != null ? Global.Instance.CurrentGlobalEvent as AmalgamationGlobalEvent : null;
                        max = globalEvent != null ? globalEvent.CurrentMaxInstanceDamage : 1000f;
                        health = Mathf.Max(0f, max - behavior.DamageTaken);
                    }
                }
                if (max <= 0f)
                {
                    var core = brain.Core;
                    if (core == null) { Hide(); return; }
                    if (brain.EnemyClass != null && !brain.EnemyClass.config.onlyUseCoreHealthForHealthbar && bar != null)
                    {
                        float ratio = Mathf.Clamp01(bar.rectTransform.localScale.x);
                        float total = brain.IsServer ? brain.totalMaxHealth : 0f;
                        if (!brain.IsServer && Plugin.BossNetwork != null &&
                            Plugin.BossNetwork.TryGet(brain, out float sharedMax, out float sharedRatio))
                        {
                            total = sharedMax;
                            ratio = sharedRatio;
                        }
                        if (total > 0f && !float.IsNaN(total) && !float.IsInfinity(total))
                        {
                            max = total;
                            health = ratio * max;
                            approximate = true;
                        }
                        else
                        {
                            max = 100f;
                            health = ratio * max;
                            percent = true;
                        }
                    }
                    else
                    {
                        health = core.Health;
                        max = core.MaxHealth;
                    }
                }
            }
            else
            {
                var rhombo = GetComponent<RhombominationHealthbar>();
                if (rhombo == null) { Hide(); return; }
                var source = rhombo.source;
                var core = source != null ? source.Core : null;
                if (core == null) { Hide(); return; }
                bar = rhombo.healthbar;
                health = core.Health;
                max = core.MaxHealth;
            }
            if (bar == null || !bar.isActiveAndEnabled || !(bar.rectTransform.parent is RectTransform parent) ||
                max <= 0f || float.IsNaN(health) || float.IsNaN(max) || float.IsInfinity(health) || float.IsInfinity(max))
            { Hide(); return; }
            if (_label == null || _parent != parent)
            {
                if (_label != null) Destroy(_label.gameObject);
                if (template == null && PlayerLook.Instance != null) template = PlayerLook.Instance.PlayerTitleText;
                if (template == null) return;
                _parent = parent;
                _label = GameText.Create(template, parent, "Boss Health Numbers");
                _label.alignment = TextAlignmentOptions.Center;
                _label.fontSize = 16f;
                _label.fontStyle = FontStyles.Bold;
                _label.color = Color.white;
                _label.maxVisibleCharacters = int.MaxValue;
                var rect = _label.rectTransform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(0f, 24f);
                _label.transform.SetAsLastSibling();
                GameText.SetShadow(_label, true, new Color(0f, 0f, 0f, 0.9f));
            }
            string text = _text.Get(health, max, 1f, 0, percent ? "{0}%" : "{0} / {1}", "");
            if (approximate)
            {
                if (!ReferenceEquals(text, _lastApproxSource)) { _lastApproxSource = text; _lastApproxText = "~" + text; }
                text = _lastApproxText;
            }
            GameHudText.SetText(_label, text);
            GameHudText.SetActive(_label, true);
        }

        private void Hide()
        {
            if (_label != null) GameHudText.SetActive(_label, false);
        }

        private void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
        }
    }
}
