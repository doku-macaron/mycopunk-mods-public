using TMPro;
using UnityEngine;
using MycopunkShared;

namespace MycopunkHealthNumbers
{
    /// <summary>Creates text labels by copying one of the game's own TextMeshPro texts, so they use its font and look.</summary>
    internal static class GameText
    {
        public static TextMeshProUGUI Create(TextMeshProUGUI template, Transform parent, string name)
        {
            var label = GameHudText.Create(template, parent, name);
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

    }
}
