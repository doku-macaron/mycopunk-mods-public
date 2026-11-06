using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkHealthRegenTimer
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
                ["regen.enabled"] = "Show health regeneration timer",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["regen.enabled"] = "体力回復までの時間を表示",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["regen.enabled"] = "体力バーの上に自動回復が始まるまでの時間を表示します。回復中や体力が満タンのときは表示しません。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
