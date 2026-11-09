using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkAbilityCooldown
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
                ["ability.cooldown"] = "Cooldown seconds on ability icons",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["ability.cooldown"] = "アビリティの再使用時間を表示",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["ability.cooldown"] = "各アビリティのアイコンに次の使用までの秒数を表示します。この変更は即時に反映されます。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
