using HarmonyLib;
using Pigeon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkAbilityCooldown
{
    /// <summary>
    /// Seconds until the next charge, and the number of charges for abilities with stacks, drawn over the ability's
    /// icon in the game's ability HUD (AbilityRechargeUI: grenade, movement ability and the like, the icons with
    /// Q / F / SPACE / CTRL under them). See <see cref="CooldownView"/> for what shows when.
    ///
    /// The ability box (measured 2026-11-04) is, in draw order: the frame (the AbilityRechargeUI itself, a
    /// BevelRectOutline, 64x64); BKG (a BevelRect, 60x60, with the charge bar Fill = chargedBackground =
    /// rechargeRect inside it); Icon (48x48, black); S (secondary cooldown); RechargedEffect; BindBKG (the keybind
    /// box); Dots. Everything here goes right above Icon, so the keybind box stays on top:
    ///  - the shade, a BevelRect copied from BKG (same rect and cut corners), with the big seconds over it (no charge);
    ///  - two small tags in BKG's corners, a dark cut-corner chip with white text, since the box is light where the
    ///    charge bar is: the seconds bottom-right (charges left) and the charge count top-right (stacks only).
    /// </summary>
    internal sealed class CooldownLabel : MonoBehaviour
    {
        private const string Prefix = "MycoCooldown";

        private static readonly System.Reflection.FieldInfo AbilityField = AccessTools.Field(typeof(AbilityRechargeUI), "ability");
        private static readonly System.Reflection.FieldInfo IconField = AccessTools.Field(typeof(AbilityRechargeUI), "icon");
        private static readonly System.Reflection.FieldInfo KeybindField = AccessTools.Field(typeof(AbilityRechargeUI), "keybindText");
        private static readonly System.Reflection.FieldInfo FillField = AccessTools.Field(typeof(AbilityRechargeUI), "chargedBackground");

        private AbilityRechargeUI _ui;
        private bool _built;
        private Graphic _shade;
        private TextMeshProUGUI _big;
        private Tag _timer, _count;
        private readonly RechargeEstimate _estimate = new RechargeEstimate();
        private readonly SecondsText _seconds = new SecondsText();

        // What is shown now, to touch the UI only on a change.
        private CooldownMode _mode = CooldownMode.None;
        private string _shownSeconds = "";
        private int _shownCount = -1;

        /// <summary>A small dark cut-corner chip with white text, pinned to a corner of the box.</summary>
        private sealed class Tag
        {
            public GameObject Root;
            public TextMeshProUGUI Text;
            public string Shown = "";

            public void Show(string text)
            {
                if (ReferenceEquals(text, Shown) || text == Shown)
                    return;
                Shown = text;
                Text.text = text;
                bool on = text.Length > 0;
                if (Root.activeSelf != on)
                    Root.SetActive(on);
            }
        }

        public static void Install(Harmony harmony)
        {
            var setup = AccessTools.Method(typeof(AbilityRechargeUI), "Setup");
            if (setup == null || AbilityField == null || IconField == null || KeybindField == null)
            {
                Plugin.Log.LogWarning("AbilityRechargeUI.Setup or its fields not found; cooldown numbers are off.");
                return;
            }
            harmony.Patch(setup, postfix: new HarmonyMethod(typeof(CooldownLabel), nameof(Setup_Postfix)));
            foreach (var ui in Object.FindObjectsByType<AbilityRechargeUI>(FindObjectsInactive.Include))
                Setup_Postfix(ui);
        }

        private void OnDestroy()
        {
            if (_shade != null) Destroy(_shade.gameObject);
            if (_big != null) Destroy(_big.gameObject);
            if (_timer?.Root != null && _timer.Root.transform.parent != null) Destroy(_timer.Root.transform.parent.gameObject);
        }

        private static void Setup_Postfix(AbilityRechargeUI __instance)
        {
            try
            {
                var label = __instance.GetComponent<CooldownLabel>();
                if (label == null)
                    label = __instance.gameObject.AddComponent<CooldownLabel>();
                label._ui = __instance;
                label._estimate.Reset(); // the pooled UI may now show another ability
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Cooldown label could not be added: " + e);
            }
        }

        private void LateUpdate()
        {
            if (_ui == null)
                return;
            try
            {
                var ability = AbilityField.GetValue(_ui) as IActivatedAbility;
                if (ability == null || !Plugin.Enabled.Value)
                {
                    if (_built)
                        Apply(CooldownView.Of(0f, 1, float.NaN), "", hideCount: true);
                    return;
                }
                float charge = ability.Charge;
                int max = ability.MaxCharges;
                float seconds = _estimate.Update(Time.time, charge, max);
                var view = CooldownView.Of(charge, max, seconds);
                if (!_built && (view.Mode != CooldownMode.None || view.ShowCount) && !Build())
                    return;
                if (_built)
                    Apply(view, _seconds.Get(seconds), hideCount: false);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Cooldown label failed and is turned off: " + e);
                enabled = false;
            }
        }

        private void Apply(CooldownView view, string seconds, bool hideCount)
        {
            if (view.Mode != _mode || !ReferenceEquals(seconds, _shownSeconds))
            {
                _mode = view.Mode;
                _shownSeconds = seconds;
                bool empty = view.Mode == CooldownMode.Empty;
                _shade.enabled = empty;
                _big.text = empty ? seconds : "";
                _timer.Show(view.Mode == CooldownMode.Recharging ? seconds : "");
            }
            int count = hideCount || !view.ShowCount ? -1 : view.Count;
            if (count != _shownCount)
            {
                _shownCount = count;
                _count.Show(count < 0 ? "" : count.ToString());
            }

            Color shade = new Color(0f, 0f, 0f, Mathf.Clamp01(Plugin.ShadeAlpha.Value));
            if (_shade.color != shade)
                _shade.color = shade;
            float size = Plugin.FontSize.Value;
            if (_big.fontSize != size)
                _big.fontSize = size;
            float small = Plugin.SmallFontSize.Value;
            if (_timer.Text.fontSize != small)
            {
                _timer.Text.fontSize = small;
                _count.Text.fontSize = small;
            }
        }

        private bool Build()
        {
            var icon = IconField.GetValue(_ui) as Image;
            var font = KeybindField.GetValue(_ui) as TextMeshProUGUI;
            if (icon == null || font == null || font.font == null)
            {
                enabled = false;
                Plugin.Log.LogWarning("AbilityRechargeUI has no icon or keybind text; no cooldown number for it.");
                return false;
            }
            // The box background: the BevelRect that holds the charge bar. Without it, cover just the icon.
            var fill = FillField?.GetValue(_ui) as Graphic;
            var bkg = fill != null && fill.transform.parent != null ? fill.transform.parent.GetComponent<BevelRect>() : null;
            var shape = bkg != null ? bkg.rectTransform : icon.rectTransform;
            var parent = icon.transform.parent;
            int at = icon.transform.GetSiblingIndex() + 1;

            // Right above the icon, in this order: shade, big seconds, the two tags.
            var shadeGo = Sibling(parent, Prefix + "Shade", shape, at);
            _shade = bkg != null ? (Graphic)Bevel(shadeGo, bkg) : shadeGo.AddComponent<Image>();
            _shade.raycastTarget = false;
            _shade.enabled = false;

            _big = Text(Sibling(parent, Prefix, shape, at + 1), font, Plugin.FontSize.Value, TextAlignmentOptions.Center);

            var corners = Sibling(parent, Prefix + "Tags", shape, at + 2);
            _timer = NewTag(corners.transform, "Timer", new Vector2(1f, 0f), bkg, font);
            _count = NewTag(corners.transform, "Count", new Vector2(1f, 1f), bkg, font);

            _mode = CooldownMode.None;
            _shownSeconds = "";
            _shownCount = -1;
            _built = true;
            return true;
        }

        /// <summary>A chip pinned inside the box at <paramref name="corner"/> (0..1 per axis), sized to its text.</summary>
        private static Tag NewTag(Transform parent, string name, Vector2 corner, BevelRect bkg, TextMeshProUGUI font)
        {
            var go = new GameObject(Prefix + name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = new Vector2(corner.x > 0.5f ? -2f : 2f, corner.y > 0.5f ? -2f : 2f);
            rt.localScale = Vector3.one;

            Graphic bg;
            if (bkg != null)
            {
                var bevel = go.AddComponent<BevelRect>();
                bevel.SetCorners(3f);
                bg = bevel;
            }
            else
            {
                bg = go.AddComponent<Image>();
            }
            bg.color = new Color(0f, 0f, 0f, 0.8f);
            bg.raycastTarget = false;

            // The chip follows its text: a fitter on the chip, padding from the layout group.
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 1, 1);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.layer = go.layer;
            textGo.transform.SetParent(rt, false);
            var text = Text(textGo, font, Plugin.SmallFontSize.Value, TextAlignmentOptions.Center);

            go.SetActive(false);
            return new Tag { Root = go, Text = text };
        }

        private static BevelRect Bevel(GameObject go, BevelRect like)
        {
            var bevel = go.AddComponent<BevelRect>();
            bevel.TopLeft = like.TopLeft;
            bevel.TopRight = like.TopRight;
            bevel.BottomLeft = like.BottomLeft;
            bevel.BottomRight = like.BottomRight;
            return bevel;
        }

        private static TextMeshProUGUI Text(GameObject go, TextMeshProUGUI font, float size, TextAlignmentOptions align)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font.font;
            t.fontSharedMaterial = font.fontSharedMaterial;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }

        /// <summary>A new child of <paramref name="parent"/> at <paramref name="index"/> with the same rect as <paramref name="shape"/>.</summary>
        private static GameObject Sibling(Transform parent, string name, RectTransform shape, int index)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = shape.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetSiblingIndex(index);
            rt.anchorMin = shape.anchorMin;
            rt.anchorMax = shape.anchorMax;
            rt.pivot = shape.pivot;
            rt.sizeDelta = shape.sizeDelta;
            rt.anchoredPosition = shape.anchoredPosition;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            return go;
        }
    }
}
