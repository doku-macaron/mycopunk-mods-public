using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkThrowPreview
{
    /// <summary>
    /// Draws a list of world points as a line on the HUD, plus a marker at the last point.
    ///
    /// The line is made of thin Images under PlayerLook.StaticReticle (the HUD rect the game also uses for its
    /// on-screen pings), placed with the local camera and the UI camera the same way Highlighter.UpdateOverlays
    /// places pings. HUD drawing needs no shader or material of our own. Segments with an end behind the camera are
    /// skipped.
    /// </summary>
    internal sealed class ArcDrawer
    {
        private const string RootName = "MycoThrowArc";

        private RectTransform _root;
        private readonly List<Image> _segments = new List<Image>();
        private Image _marker;
        private int _used;

        public void Draw(List<Vector3> points, bool hasEnd, Color color, float thickness, float dashLength, float gapLength)
        {
            _used = 0;
            var look = PlayerLook.Instance;
            var cam = PlayerLook.LocalCamera;
            if (look == null || cam == null || points.Count < 2 || !EnsureRoot(look))
            {
                Hide();
                return;
            }
            var uiCam = PlayerLook.LocalUICamera;
            var parent = (RectTransform)_root.parent;

            // Dashed: draw `dash`, skip `gap`, measured along the line on the HUD and carried across points.
            float dash = Mathf.Max(1f, dashLength), gap = Mathf.Max(0f, gapLength);
            float phase = 0f; // distance into the current dash + gap period
            bool prevOk = Project(cam, uiCam, parent, points[0], out Vector2 prev);
            for (int i = 1; i < points.Count; i++)
            {
                bool ok = Project(cam, uiCam, parent, points[i], out Vector2 cur);
                if (ok && prevOk)
                    phase = Dashed(prev, cur, phase, dash, gap, color, thickness);
                prev = cur;
                prevOk = ok;
            }
            for (int i = _used; i < _segments.Count; i++)
                if (_segments[i].enabled)
                    _segments[i].enabled = false;

            if (hasEnd && prevOk)
            {
                if (_marker == null)
                    _marker = NewImage("End");
                var rt = _marker.rectTransform;
                rt.localPosition = prev;
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                float s = thickness * 4f;
                rt.sizeDelta = new Vector2(s, s);
                _marker.color = color;
                _marker.enabled = true;
            }
            else if (_marker != null && _marker.enabled)
            {
                _marker.enabled = false;
            }
        }

        public void Hide()
        {
            if (_root == null)
                return;
            foreach (var s in _segments)
                if (s != null && s.enabled)
                    s.enabled = false;
            if (_marker != null && _marker.enabled)
                _marker.enabled = false;
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _segments.Clear();
            _marker = null;
        }

        private bool EnsureRoot(PlayerLook look)
        {
            if (_root != null)
                return true;
            var reticle = look.StaticReticle;
            if (reticle == null)
                return false;
            var go = new GameObject(RootName, typeof(RectTransform));
            go.layer = reticle.gameObject.layer;
            _root = (RectTransform)go.transform;
            _root.SetParent(reticle, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            _segments.Clear(); // the old ones went with the old HUD
            _marker = null;
            return true;
        }

        private static bool Project(Camera cam, Camera uiCam, RectTransform parent, Vector3 world, out Vector2 local)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            local = default;
            if (s.z <= 0.05f)
                return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, s, uiCam, out local);
        }

        /// <summary>Draws the dash parts of a→b, starting <paramref name="phase"/> into the period; returns the phase at b.</summary>
        private float Dashed(Vector2 a, Vector2 b, float phase, float dash, float gap, Color color, float thickness)
        {
            float len = (b - a).magnitude;
            if (len <= 0f)
                return phase;
            if (gap <= 0f)
            {
                Segment(a, b, color, thickness);
                return 0f;
            }
            float period = dash + gap;
            float t = 0f;
            while (t < len)
            {
                if (phase < dash)
                {
                    float d = Mathf.Min(dash - phase, len - t);
                    Segment(Vector2.Lerp(a, b, t / len), Vector2.Lerp(a, b, (t + d) / len), color, thickness);
                    t += d;
                    phase += d;
                }
                else
                {
                    float g = Mathf.Min(period - phase, len - t);
                    t += g;
                    phase += g;
                }
                if (phase >= period)
                    phase -= period;
            }
            return phase;
        }

        private void Segment(Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
                return;
            Image img;
            if (_used < _segments.Count)
            {
                img = _segments[_used];
            }
            else
            {
                img = NewImage("Seg");
                _segments.Add(img);
            }
            _used++;
            var rt = img.rectTransform;
            rt.localPosition = a;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            rt.sizeDelta = new Vector2(len, thickness);
            if (img.color != color)
                img.color = color;
            if (!img.enabled)
                img.enabled = true;
        }

        private Image NewImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = _root.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = name == "Seg" ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }
    }
}
