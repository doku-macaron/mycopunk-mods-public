using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkShared
{
    /// <summary>
    /// The game's TeammateHUD as an <see cref="ITeammateHud"/>, and the calls mods use. The layout itself is in
    /// <see cref="TeammateHudLines"/>; this only reads and moves the Unity objects.
    /// </summary>
    public sealed class UnityTeammateHud : ITeammateHud
    {
        /// <summary>
        /// Kept for the mods that check it. The TeammateHUD fields this needs are used directly (publicized game
        /// assembly): a game update that removes one makes the calls throw MissingFieldException instead, so this is always true.
        /// </summary>
        public static bool Available => true;

        /// <summary>
        /// The line <paramref name="id"/> under the teammate's name (created if needed). Call from a
        /// TeammateHUD.Initialize postfix or later. Smaller <paramref name="order"/> is higher. Null when it cannot be done.
        /// </summary>
        public static TextMeshProUGUI AddText(TeammateHUD hud, string id, int order)
        {
            var view = For(hud);
            return view != null ? ((Line)TeammateHudLines.Add(view, id, order)).Text : null;
        }

        public static void Remove(TeammateHUD hud, string id)
        {
            var view = For(hud);
            if (view != null) TeammateHudLines.Remove(view, id);
        }

        /// <summary>Puts the lines back under the name, for when the name text moved.</summary>
        public static void Place(TeammateHUD hud)
        {
            var view = For(hud);
            if (view != null) TeammateHudLines.Place(view);
        }

        private static UnityTeammateHud For(TeammateHUD hud)
        {
            if (!Available || hud == null) return null;
            var name = hud.nameText;
            return name != null ? new UnityTeammateHud(hud, name) : null;
        }

        private sealed class Line : IHudLine
        {
            public readonly TextMeshProUGUI Text;
            public Line(TextMeshProUGUI text) { Text = text; }
            public string Name { get { return Text.name; } set { Text.name = value; } }
        }

        private readonly TeammateHUD hud;
        private readonly TextMeshProUGUI name;

        private UnityTeammateHud(TeammateHUD hud, TextMeshProUGUI name)
        {
            this.hud = hud;
            this.name = name;
        }

        public float NameFontSize => name.fontSize;

        public bool HasPrevious => hud.Previous != null && hud.transform is RectTransform;

        public float Offset
        {
            get { return hud.offset; }
            set { hud.offset = value; }
        }

        public string Marker
        {
            get { var m = FindMarker(); return m != null ? m.name : null; }
            set
            {
                Transform m = FindMarker();
                if (value == null)
                {
                    if (m == null) return;
                    m.name = "MycoHudLines (removed)";
                    Object.Destroy(m.gameObject);
                    return;
                }
                if (m == null)
                {
                    var go = new GameObject(value);
                    go.transform.SetParent(hud.transform, false);
                    go.SetActive(false);
                    return;
                }
                m.name = value;
            }
        }

        private Transform FindMarker()
        {
            Transform t = hud.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child.name.StartsWith(TeammateHudLines.MarkerPrefix, System.StringComparison.Ordinal)) return child;
            }
            return null;
        }

        public IEnumerable<IHudLine> Siblings
        {
            get
            {
                Transform parent = name.transform.parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var text = parent.GetChild(i).GetComponent<TextMeshProUGUI>();
                    if (text != null && text != name) yield return new Line(text);
                }
            }
        }

        public IHudLine CreateLine(string lineName)
        {
            TextMeshProUGUI line = Object.Instantiate(name, name.transform.parent, false);
            line.name = lineName;
            line.text = "";
            line.raycastTarget = false;
            line.richText = false;
            line.enableAutoSizing = false;
            line.fontSize = name.fontSize;
            line.textWrappingMode = TextWrappingModes.NoWrap;
            line.overflowMode = TextOverflowModes.Overflow;
            line.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 360);
            return new Line(line);
        }

        public void DestroyLine(IHudLine line)
        {
            var text = ((Line)line).Text;
            text.gameObject.SetActive(false);
            Object.Destroy(text.gameObject);
        }

        public void MoveLower(float dy)
        {
            if (dy == 0) return;
            var candidates = new List<RectTransform>();
            foreach (Graphic g in new Graphic[] { hud.classText, hud.titleText, hud.loadingBarParent })
                if (g != null) candidates.Add(g.rectTransform);
            foreach (var item in TeammateHudLines.TopLevel(candidates, name.rectTransform, (a, b) => a.IsChildOf(b)))
                item.anchoredPosition += new Vector2(0, dy);
        }

        public void MoveRoot(float dy)
        {
            if (hud.transform is RectTransform root) root.anchoredPosition += new Vector2(0, dy);
        }

        public void PlaceLine(IHudLine line, float below)
        {
            ((Line)line).Text.rectTransform.anchoredPosition = name.rectTransform.anchoredPosition + new Vector2(0, -below);
        }
    }
}
