using TMPro;
using MycopunkShared;
using UnityEngine;
using UnityEngine.UI;

internal sealed class LocalDpsLabel
{
    private TextMeshProUGUI template;
    private TextMeshProUGUI label;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly StatsText text = new StatsText();

    public bool Visible { get { return label != null && label.gameObject.activeInHierarchy; } }

    public void Refresh(TextMeshProUGUI title, EquippableHUDOverlay[] overlays, CombatStats stats)
    {
        RectTransform parent = null;
        Vector3 topLeft = Vector3.zero;
        bool found = false;
        if (title != null && overlays != null)
        {
            foreach (EquippableHUDOverlay overlay in overlays)
            {
                if (overlay == null || !overlay.gameObject.activeInHierarchy) continue;
                RectTransform box = overlay.transform as RectTransform;
                if (box == null || !(box.parent is RectTransform)) continue;
                if (parent == null) parent = (RectTransform)box.parent;
                box.GetWorldCorners(corners);
                Vector3 corner = parent.InverseTransformPoint(corners[1]);
                if (!found || corner.y > topLeft.y) topLeft = corner;
                found = true;
            }
        }
        if (parent == null) { Clear(); return; }

        if (template != title || label == null || label.transform.parent != parent)
        {
            Clear();
            template = title;
            label = Object.Instantiate(title, parent, false);
            label.name = "DPS Checker Local";
            label.raycastTarget = false;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.enableAutoSizing = false;
            label.fontSize = Mathf.Max(title.fontSize * 1.2f, 20f);
            Outline outline = label.GetComponent<Outline>();
            if (outline == null) outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.9f);
            outline.effectDistance = new Vector2(1, -1);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.pivot = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(420f, label.fontSize * 1.5f);
            label.gameObject.SetActive(true);
        }

        string value = text.Get(stats, false, false, true);
        GameHudText.SetText(label, value);
        label.color = Color.white;
        label.rectTransform.localPosition = topLeft + new Vector3(0, 4f, 0);
    }

    public void Clear()
    {
        if (label != null) Object.Destroy(label.gameObject);
        label = null;
        template = null;
    }
}
