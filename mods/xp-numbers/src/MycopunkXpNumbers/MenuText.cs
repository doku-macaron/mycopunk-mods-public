using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkXpNumbers
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
                ["xp.level"] = "XP numbers on level bars",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["xp.level"] = "レベルバーにXPを表示",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["xp.level"] = "レベルバーの横に、現在のレベルで得たXPと、次のレベルまでに必要なXPを表示します。TAB の画面、ビルド画面、ミッションのリザルト画面に表示されます。次にバーが更新されたときから反映されます。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
