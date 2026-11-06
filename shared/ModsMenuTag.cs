using System;
using System.Collections.Generic;

namespace MycopunkShared
{
    /// <summary>
    /// Marks a config entry for the MODS tab in the game's options window (shared/Menu/ModsMenu.cs builds it).
    /// Pass the result as the tags of the entry's ConfigDescription:
    /// <c>new ConfigDescription("...", null, ModsMenuTag.Label("load.navmesh"))</c>.
    /// Only bool entries are shown for now. The tags are plain strings, so a mod needs no shared type for this, and
    /// a mod without the menu code still gets its entries listed when another mod builds the tab.
    /// </summary>
    public static class ModsMenuTag
    {
        public const string Tag = "MycopunkShared.ModsMenu";

        /// <summary>
        /// The tags for one entry, from the calling mod's own tables (language → key → text): names, where "en" is
        /// required, and optional hover descriptions. Each mod keeps its text in its own MenuText.cs, so adding an
        /// entry never needs a new version of the kit.
        /// </summary>
        public static object[] Label(string key,
            IDictionary<string, Dictionary<string, string>> names,
            IDictionary<string, Dictionary<string, string>> descriptions = null)
        {
            if (names == null || !names.TryGetValue("en", out var en) || !en.TryGetValue(key, out string english))
                throw new ArgumentException("Missing English MODS menu text: " + key);
            var tags = new List<object> { Tag, Tag + ":" + english };
            foreach (var language in names)
                if (language.Key != "en" && language.Value.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text))
                    tags.Add(Tag + "." + language.Key + ":" + text);
            if (descriptions != null)
                foreach (var language in descriptions)
                    if (language.Value.TryGetValue(key, out string description) && !string.IsNullOrEmpty(description))
                        tags.Add(Tag + ".description." + language.Key + ":" + description);
            return tags.ToArray();
        }

        /// <summary>The MODS tab language for the game's TextBlocks.LanguageIndex (8 = Japanese, else English).</summary>
        public static string Language(int gameLanguageIndex) => gameLanguageIndex == 8 ? "ja" : "en";

        public static string Description(object[] tags, string language, string fallback)
        {
            string prefix = Tag + ".description." + language + ":";
            if (tags != null)
                foreach (var tag in tags)
                    if (tag is string s && s.StartsWith(prefix, StringComparison.Ordinal) && s.Length > prefix.Length)
                        return s.Substring(prefix.Length);
            return fallback;
        }

        /// <summary>The label from the tags, or null when the entry is not marked.</summary>
        public static string Find(object[] tags, string language = "en")
        {
            if (tags == null) return null;
            string label = null;
            string translation = null;
            bool marked = false;
            foreach (var t in tags)
            {
                if (!(t is string s)) continue;
                if (s == Tag) marked = true;
                else if (s.StartsWith(Tag + ":", StringComparison.Ordinal)) { marked = true; label = s.Substring(Tag.Length + 1); }
                else if (s.StartsWith(Tag + "." + language + ":", StringComparison.Ordinal)) translation = s.Substring(Tag.Length + language.Length + 2);
            }
            return marked ? (!string.IsNullOrEmpty(translation) ? translation : label ?? "") : null;
        }
    }
}
