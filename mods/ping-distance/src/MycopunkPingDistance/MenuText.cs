using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkPingDistance
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
                ["ping.distance"] = "Distance under pings",
                ["ping.color"] = "Distance in ping color",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["ping.distance"] = "ピンの下に距離を表示",
                ["ping.color"] = "距離をピンと同じ色で表示",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["ping.distance"] = "自分や味方のピン、マップ上のピン、目標マーカーの下に距離をメートルで表示します。この変更は即時に反映されます。",
                ["ping.color"] = "距離を白ではなくピンと同じ色で表示します。白のほうが多くの地面で読みやすくなります。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
