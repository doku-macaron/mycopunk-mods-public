using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkMinimapNorthUp
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
                ["minimap.north"] = "North-up minimap",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["minimap.north"] = "ミニマップを北向きに固定",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["minimap.north"] = "ミニマップの北を上に固定し、マップの代わりにプレイヤーの矢印を回転させます。変更はすぐに反映されます。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
