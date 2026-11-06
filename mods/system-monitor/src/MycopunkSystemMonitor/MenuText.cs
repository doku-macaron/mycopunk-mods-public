using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkSystemMonitor
{
    /// <summary>
    /// This mod's names and hover descriptions for the MODS tab, by language ("en" is required, others fall back to
    /// it). Mycopunk Mod Kit builds the tab; the text travels in the config entries' tags.
    /// </summary>
    internal static class MenuText
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Names = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>
            {
                ["monitor.top"] = "Show at top",
                ["monitor.right"] = "Show on right",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["monitor.top"] = "画面の上に表示",
                ["monitor.right"] = "画面の右に表示",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["monitor.top"] = "モニターを画面の下ではなく上に表示します。",
                ["monitor.right"] = "モニターを画面の左ではなく右に表示します。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
