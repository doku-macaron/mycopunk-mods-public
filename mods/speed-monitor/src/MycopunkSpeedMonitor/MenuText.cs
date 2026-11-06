using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkSpeedMonitor
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
                ["speed.position"] = "Calculate speed from position",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["speed.position"] = "実際の移動距離から速度を計算",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["speed.position"] = "位置の変化から移動速度を計算します。オフの場合はゲームの速度値を表示します。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
