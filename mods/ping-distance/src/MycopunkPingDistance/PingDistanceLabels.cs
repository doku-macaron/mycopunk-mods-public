using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkPingDistance
{
    /// <summary>
    /// Distance in metres under every on-screen ping.
    ///
    /// Highlighter.pings holds every ping the HUD draws: a player's ping (10 s), a pin placed on the map
    /// (AddMapPing, persistent) and objective markers (AddWaypointPing, persistent). For each one with showInWorld,
    /// Highlighter.UpdateOverlays moves ping.graphic (an Image on the HUD) to the target, or to the screen edge when
    /// it is behind, and changes its sizeDelta and alpha; it never rotates it. The world position below is the same
    /// as UpdateOverlays computes. The label is a child of that Image, anchored at its centre so the size pulse does
    /// not move it, and it takes the ping's colour (and so its fade). Ping graphics are pooled, so labels are kept
    /// per Image and blanked when their ping is gone.
    /// </summary>
    internal sealed class PingDistanceLabels : MonoBehaviour
    {
        private const string LabelName = "MycoPingDistance";

        private static readonly FieldInfo TopText = AccessTools.Field(typeof(Minimap), "topText");

        private sealed class Label
        {
            public TextMeshProUGUI Text;
            public readonly DistanceText Distance = new DistanceText();
            public string Shown;
            public int Frame;
        }

        private readonly Dictionary<Image, Label> _labels = new Dictionary<Image, Label>();
        private readonly List<Image> _stale = new List<Image>();
        private bool _failed;

        private void OnDestroy()
        {
            foreach (var label in _labels.Values)
                if (label.Text != null) Destroy(label.Text.gameObject);
            _labels.Clear();
        }

        private void LateUpdate()
        {
            if (_failed)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("Ping distance labels failed and are turned off: " + e);
            }
        }

        private void Tick()
        {
            var pings = Highlighter.pings;
            bool on = Plugin.PingDistance.Value;
            int frame = Time.frameCount;
            if (pings != null && (on || _labels.Count > 0))
            {
                Vector3 eye = PlayerLook.Position;
                for (int i = 0; i < pings.Count; i++)
                {
                    var p = pings[i];
                    if (p == null || p.graphic == null)
                        continue;
                    if (!on || !p.showInWorld)
                        continue; // blanked below with the stale ones
                    var label = Get(p.graphic);
                    if (label == null)
                        continue;
                    label.Frame = frame;

                    Vector3 pos = p.target == null ? p.localPosition
                        : p.isPositionOffset ? p.target.position + p.localPosition
                        : p.target.TransformPoint(p.localPosition);
                    string text = label.Distance.Get(Vector3.Distance(eye, pos), Plugin.MinDistance.Value);
                    if (!ReferenceEquals(text, label.Shown))
                    {
                        label.Shown = text;
                        label.Text.text = text;
                    }

                    var rt = label.Text.rectTransform;
                    var at = new Vector2(0f, -(p.maxSize * 0.5f + 2f));
                    if (rt.anchoredPosition != at)
                        rt.anchoredPosition = at;
                    float size = Plugin.FontSize.Value;
                    if (label.Text.fontSize != size)
                        label.Text.fontSize = size;
                    // White by default (readable on any ground); the ping's alpha either way, so it fades with it.
                    Color g = p.graphic.color;
                    Color c = Plugin.UsePingColor.Value ? g : new Color(1f, 1f, 1f, g.a);
                    if (label.Text.color != c)
                        label.Text.color = c;
                }
            }

            // Blank labels whose ping is gone (pooled graphic) and forget the ones of destroyed graphics.
            foreach (var kv in _labels)
            {
                if (kv.Key == null || kv.Value.Text == null)
                {
                    _stale.Add(kv.Key);
                    continue;
                }
                if (kv.Value.Frame != frame && kv.Value.Shown != "")
                {
                    kv.Value.Shown = "";
                    kv.Value.Text.text = "";
                }
            }
            if (_stale.Count > 0)
            {
                foreach (var k in _stale)
                    _labels.Remove(k);
                _stale.Clear();
            }
        }

        private Label Get(Image graphic)
        {
            if (_labels.TryGetValue(graphic, out var label))
                return label;

            var source = FontSource();
            if (source == null)
                return null; // the HUD is not up yet; try again next frame

            var go = new GameObject(LabelName, typeof(RectTransform));
            go.layer = graphic.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(graphic.rectTransform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(200f, 30f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = source.font;
            tmp.fontSharedMaterial = source.fontSharedMaterial;
            tmp.fontSize = Plugin.FontSize.Value;
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = "";

            label = new Label { Text = tmp, Shown = "" };
            _labels[graphic] = label;
            return label;
        }

        /// <summary>
        /// A HUD text to copy the font from: the minimap's top text, else the HUD title under the crosshair
        /// (PlayerLook.PlayerTitleText), which also exists in the hub.
        /// </summary>
        private static TextMeshProUGUI FontSource()
        {
            TextMeshProUGUI t = null;
            if (TopText != null && Highlighter.TryGetInstance(out var h) && h.Map != null)
                t = TopText.GetValue(h.Map) as TextMeshProUGUI;
            if (t == null || t.font == null)
            {
                var look = PlayerLook.Instance;
                t = look != null ? look.PlayerTitleText : null;
            }
            return t != null && t.font != null ? t : null;
        }
    }
}
