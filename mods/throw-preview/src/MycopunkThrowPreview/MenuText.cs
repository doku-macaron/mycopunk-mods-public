using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkThrowPreview
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
                ["throw.grenade"] = "Hold to aim grenades, release to throw",
                ["throw.arc"] = "Arc for thrown objects",
                ["throw.color"] = "Arc color",
                ["throw.revive_color"] = "Arc color when it revives",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["throw.grenade"] = "グレネードを長押しで狙い、離して投げる",
                ["throw.arc"] = "投げる物の軌道を表示",
                ["throw.color"] = "軌道の色",
                ["throw.revive_color"] = "蘇生できるときの軌道の色",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["throw.grenade"] = "グレネードキーを押している間は投げる場所を表示し、離すと投げます。短く押した場合はすぐに投げます。この変更は即時に反映されます。",
                ["throw.arc"] = "持っている物を投げるためにチャージしている間、軌道を表示します。この変更は即時に反映されます。",
                ["throw.color"] = "軌道の線の色です。不透明度は設定ファイルの値のままです。",
                ["throw.revive_color"] = "味方のパーツを投げて、同じ味方の別のパーツに繋がる（蘇生が進む）ときの軌道の色です。不透明度は設定ファイルの値のままです。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
