using System;
using System.Globalization;
using MycopunkShared;
using TMPro;
using UnityEngine;

internal sealed class BurstLabel
{
    private TextMeshProUGUI label;
    private Canvas canvas;
    private string shown;

    public void Refresh(TextMeshProUGUI template, double damage, float remaining)
    {
        if (damage <= 0 || template == null || template.canvas == null ||
            !template.canvas.rootCanvas.isActiveAndEnabled || HudVisibility.ResultsOpen())
        {
            if (label != null) label.gameObject.SetActive(false);
            return;
        }
        var root = template.canvas.rootCanvas;
        if (label == null || canvas != root)
        {
            Clear();
            canvas = root;
            label = GameHudText.Create(template, root.transform, "DPS Checker Burst Damage");
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(32f, -28f);
            rect.sizeDelta = new Vector2(220f, 28f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            label.transform.SetAsLastSibling();
        }
        string text = Math.Ceiling(damage).ToString("0", CultureInfo.InvariantCulture);
        if (shown != text) { shown = text; label.text = text; }
        var color = Color.white;
        color.a = Mathf.Clamp01(remaining / 0.8f);
        label.color = color;
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
    }

    public void Clear()
    {
        if (label != null) UnityEngine.Object.Destroy(label.gameObject);
        label = null;
        canvas = null;
        shown = null;
    }
}
