using TMPro;
using UnityEngine;

namespace MycopunkHealthNumbers
{
    /// <summary>Creates text labels by copying one of the game's own TextMeshPro texts, so they use its font and look.</summary>
    internal static class GameText
    {
        public static TextMeshProUGUI Create(TextMeshProUGUI template, Transform parent, string name)
        {
            var label = Object.Instantiate(template, parent, false);
            label.name = name;
            // The copy may bring children of the template along; only the text itself is wanted.
            for (int i = label.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(label.transform.GetChild(i).gameObject);
            label.raycastTarget = false;
            label.richText = false;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            label.text = "";
            label.gameObject.SetActive(true);
            return label;
        }

        /// <summary>Turns a soft drop shadow (TMP underlay) on or off. Uses a per-label material instance.</summary>
        public static void SetShadow(TextMeshProUGUI label, bool on, Color color)
        {
            var mat = label.fontMaterial;
            if (mat == null)
                return;
            if (on)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, color);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            }
        }

        public static void SetText(TextMeshProUGUI label, string text)
        {
            if (!ReferenceEquals(label.text, text) && label.text != text)
                label.text = text;
        }

        public static void SetActive(TextMeshProUGUI label, bool on)
        {
            if (label.gameObject.activeSelf != on)
                label.gameObject.SetActive(on);
        }
    }
}
