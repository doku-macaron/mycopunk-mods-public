using TMPro;
using UnityEngine;

namespace MycopunkShared
{
    public static class GameHudText
    {
        public static TextMeshProUGUI Create(TextMeshProUGUI template, Transform parent, string name)
        {
            var label = Object.Instantiate(template, parent, false);
            label.name = name;
            for (int i = label.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(label.transform.GetChild(i).gameObject);
            label.raycastTarget = false;
            label.richText = false;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        public static void SetText(TextMeshProUGUI label, string text)
        {
            if (label.text != text) label.text = text;
        }

        public static void SetActive(TextMeshProUGUI label, bool active)
        {
            if (label.gameObject.activeSelf != active) label.gameObject.SetActive(active);
        }
    }
}
