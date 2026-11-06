using System.Collections.Generic;
using MycopunkShared;

namespace UpgradeLagFix
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
                ["perf.cache"] = "Upgrade lookup cache",
                ["perf.verify"] = "Verify the cache (debug, slow)",
                ["perf.cleanup"] = "Skip redundant cleanup",
                ["perf.prepare"] = "Skip redundant prepare",
                ["perf.sort"] = "Skip sorting a sorted list",
                ["perf.stacking"] = "Stacking table",
                ["perf.hotkeys"] = "F6-F11 hotkeys",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["perf.cache"] = "アップグレード検索のキャッシュ",
                ["perf.verify"] = "キャッシュの検証（デバッグ・低速）",
                ["perf.cleanup"] = "不要なクリーンアップを省略",
                ["perf.prepare"] = "不要な準備処理を省略",
                ["perf.sort"] = "整列済みリストの並べ替えを省略",
                ["perf.stacking"] = "重ねがけの順位表",
                ["perf.hotkeys"] = "F6〜F11 のホットキー",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["perf.cache"] = "アップグレード検索にキャッシュを使います。ゲーム中は F9 でも切り替えられます。",
                ["perf.verify"] = "デバッグ用。元の検索結果とキャッシュを毎回比較して不一致を記録します。元の結果を使うため安全ですが低速です。",
                ["perf.cleanup"] = "何も削除されないことが確実なときに、アップグレードの不要な整理処理を省略します。キャッシュ検証中は元の処理も実行します。F8 でも切り替えられます。",
                ["perf.prepare"] = "何も削除されないことが確実なときに、アップグレードの破棄チェックを省略します。F7 で並べ替えの省略と一緒に切り替えられます。",
                ["perf.sort"] = "アップグレードがすでに整列済みのときは並べ替えを省略します。F7 で準備処理の省略と一緒に切り替えられます。",
                ["perf.stacking"] = "アップグレードの重ねがけ順を装備ごとの表から取得します。安全に省略できる場合だけ使い、準備処理と整理処理の省略が必要です。F6 でも切り替えられます。",
                ["perf.hotkeys"] = "F6〜F9 で各最適化を切り替え、F10 でベンチマークを実行、F11 で統計を出力・リセットします。既定ではオフです。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
