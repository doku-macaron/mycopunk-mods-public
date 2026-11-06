using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkQuickTimers
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
                ["quick.startup"] = "Skip startup animations",
                ["quick.drop"] = "Shorten fixed-map drop",
                ["quick.extraction"] = "Shorten extraction flight",
                ["quick.mission_start"] = "Shorten mission start delay",
                ["quick.mission_end"] = "Shorten mission end delay",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["quick.startup"] = "起動時の演出をスキップ",
                ["quick.drop"] = "固定マップの降下待ちを短縮",
                ["quick.extraction"] = "脱出ポッドの帰還演出を短縮",
                ["quick.mission_start"] = "ミッション開始前の待ちを短縮",
                ["quick.mission_end"] = "ミッション終了後の待ちを短縮",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["quick.startup"] = "起動時の演出をスキップし、音声の読み込み中にタイトルを表示します。変更は次回のゲーム起動時から反映されます。",
                ["quick.drop"] = "固定マップの標準の降下時間を短縮します。ホストの設定が全員に適用され、地形生成は待ちます。",
                ["quick.extraction"] = "ホストがミッション統計を受信し、参加者全員が生存してポッド内にいる場合に帰還演出を短縮します。それ以外は元の時間になります。",
                ["quick.mission_start"] = "全員の読み込みを待った後の固定時間を短縮します。ホストの設定が全員に適用されます。",
                ["quick.mission_end"] = "ホストが全員のミッション統計を集めた後、報酬処理前の自分の待ち時間を短縮します。確認できない場合は元の待ち時間になります。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
