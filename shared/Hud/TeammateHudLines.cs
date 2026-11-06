using System;
using System.Collections.Generic;
using System.Globalization;

namespace MycopunkShared
{
    /// <summary>One extra text line under a teammate's name.</summary>
    public interface IHudLine
    {
        /// <summary>The object's name; the layout keeps its order and id in it.</summary>
        string Name { get; set; }
    }

    /// <summary>
    /// What the line layout needs from one teammate HUD. <see cref="UnityTeammateHud"/> is the game's; tests use
    /// their own. All positions are y in the HUD's units, up positive.
    /// </summary>
    public interface ITeammateHud
    {
        float NameFontSize { get; }
        /// <summary>The game places a HUD that has a previous one at previous + offset (Initialize, MoveDown).</summary>
        bool HasPrevious { get; }
        float Offset { get; set; }
        /// <summary>Name of the marker object that records the applied layout; null = none (setting null removes it).</summary>
        string Marker { get; set; }
        /// <summary>The texts next to the name, in any order. Lines being destroyed may still be listed.</summary>
        IEnumerable<IHudLine> Siblings { get; }
        /// <summary>A copy of the name text next to it, empty, with the given name.</summary>
        IHudLine CreateLine(string name);
        void DestroyLine(IHudLine line);
        /// <summary>Moves the class, title and loading bar (see <see cref="TeammateHudLines.TopLevel{T}"/>).</summary>
        void MoveLower(float dy);
        void MoveRoot(float dy);
        /// <summary>Puts the line at the name's position, <paramref name="below"/> lower.</summary>
        void PlaceLine(IHudLine line, float below);
    }

    /// <summary>
    /// Extra text lines under a teammate's name, shared by every mod that adds one.
    ///
    /// Each mod compiles its own copy of this class, so nothing is kept in static fields. Everything lives in the
    /// HUD itself and any copy can rebuild the layout from it:
    ///  - a line is named "MycoLine|&lt;order&gt;|&lt;id&gt;";
    ///  - how far the layout currently moves things is written in the name of a marker object.
    /// The layout: lines sorted by order (then id) one below the other under the name; the class, title and loading
    /// bar moved down by the lines' height; TeammateHUD.offset grown by the same amount, so the HUDs below make room.
    /// A HUD with a previous one always carries the addition in its own position (the game puts it at
    /// previous + offset) and one without never does; the layout keeps to that instead of remembering how far it
    /// moved the HUD. Re-running it is a no-op, so the order in which mods add their lines does not matter.
    /// </summary>
    public static class TeammateHudLines
    {
        public const string LinePrefix = "MycoLine|";
        public const string MarkerPrefix = "MycoHudLines|";
        public const string RemovedName = "MycoLine (removed)";

        /// <summary>The line <paramref name="id"/>, created if needed, after laying the lines out again. Smaller order is higher.</summary>
        public static IHudLine Add(ITeammateHud hud, string id, int order)
        {
            IHudLine line = Find(hud, id);
            if (line == null)
                line = hud.CreateLine(LinePrefix + order.ToString("D4", CultureInfo.InvariantCulture) + "|" + id);
            Layout(hud);
            return line;
        }

        /// <summary>Removes the line <paramref name="id"/> (if there) and closes the gap.</summary>
        public static void Remove(ITeammateHud hud, string id)
        {
            IHudLine line = Find(hud, id);
            if (line == null) return;
            // Destroying may only happen later (Unity: end of frame); take it out of the layout now.
            line.Name = RemovedName;
            hud.DestroyLine(line);
            Layout(hud);
        }

        /// <summary>Puts the lines back under the name, for when the name moved. Does not touch anything else.</summary>
        public static void Place(ITeammateHud hud)
        {
            State state;
            if (!TryParse(hud.Marker, out state)) return;
            PlaceLines(hud, Lines(hud), state.LineHeight);
        }

        /// <summary>
        /// The items to move: without <paramref name="name"/>, anything that contains it, duplicates, and items inside
        /// another candidate (moving the parent already moves them). <paramref name="isInside"/>(a, b): a is b or below it.
        /// </summary>
        public static List<T> TopLevel<T>(IList<T> candidates, T name, Func<T, T, bool> isInside) where T : class
        {
            var result = new List<T>();
            foreach (var item in candidates)
            {
                if (item == null || item == name || isInside(name, item) || result.Contains(item)) continue;
                bool insideOther = false;
                foreach (var other in candidates)
                    if (other != null && other != item && other != name && !isInside(name, other) && isInside(item, other)) insideOther = true;
                if (!insideOther) result.Add(item);
            }
            return result;
        }

        public static float LineHeight(float nameFontSize) => Math.Max(nameFontSize * 1.15f, 18f);

        private static IHudLine Find(ITeammateHud hud, string id)
        {
            foreach (var line in Lines(hud))
                if (line.Name.EndsWith("|" + id, StringComparison.Ordinal)) return line;
            return null;
        }

        private static List<IHudLine> Lines(ITeammateHud hud)
        {
            var result = new List<IHudLine>();
            foreach (var s in hud.Siblings)
                if (s != null && s.Name != null && s.Name.StartsWith(LinePrefix, StringComparison.Ordinal)) result.Add(s);
            // The order is zero-padded in the name, so the names sort by order, then id.
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        private static void PlaceLines(ITeammateHud hud, List<IHudLine> lines, float lineHeight)
        {
            for (int i = 0; i < lines.Count; i++) hud.PlaceLine(lines[i], (i + 1) * lineHeight);
        }

        private struct State
        {
            public float Lower;      // added to the lower items' y
            public float Offset;     // added to TeammateHUD.offset (and to the HUD's y when it has a previous HUD)
            public float LineHeight;
        }

        private static void Layout(ITeammateHud hud)
        {
            State state;
            TryParse(hud.Marker, out state);
            float offset = hud.Offset;

            // Undo what is applied now.
            hud.MoveLower(-state.Lower);
            offset -= state.Offset;
            if (hud.HasPrevious) hud.MoveRoot(-state.Offset);

            var lines = Lines(hud);
            float lineHeight = LineHeight(hud.NameFontSize);
            float total = lines.Count * lineHeight;
            PlaceLines(hud, lines, lineHeight);

            state.Lower = -total;
            // The HUDs stack downwards when offset is negative.
            state.Offset = offset < 0 ? -total : total;
            state.LineHeight = lineHeight;
            hud.MoveLower(state.Lower);
            hud.Offset = offset + state.Offset;
            if (hud.HasPrevious) hud.MoveRoot(state.Offset);
            hud.Marker = lines.Count == 0 ? null : MarkerPrefix + Format(state.Lower) + "|" + Format(state.Offset) + "|" + Format(state.LineHeight);
        }

        private static bool TryParse(string marker, out State state)
        {
            state = new State();
            if (marker == null || !marker.StartsWith(MarkerPrefix, StringComparison.Ordinal)) return false;
            var p = marker.Split('|');
            if (p.Length < 4) return false;
            state.Lower = Parse(p[1]);
            state.Offset = Parse(p[2]);
            state.LineHeight = Parse(p[3]);
            return true;
        }

        // "R" round-trips, so undoing subtracts exactly what was added.
        private static string Format(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static float Parse(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }
}
