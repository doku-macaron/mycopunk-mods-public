using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkHealthNumbers
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
                ["health.enabled"] = "Show health numbers",
                ["health.dead"] = "Hide while dead",
                ["health.bar"] = "Hide with the health bar",
                ["health.bold"] = "Bold",
                ["health.color"] = "Text color",
                ["health.shadow"] = "Shadow",
                ["health.teammates"] = "Teammates' health",
                ["health.boss"] = "Show boss health numbers",
                ["health.name_color"] = "Teammates' health in their name color",
                ["health.damage_log"] = "Damage log (debug)",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["health.enabled"] = "体力を数値で表示",
                ["health.dead"] = "死亡中は非表示",
                ["health.bar"] = "体力バーと一緒に非表示にする",
                ["health.bold"] = "太字",
                ["health.color"] = "文字の色",
                ["health.shadow"] = "文字の影",
                ["health.teammates"] = "味方の体力を表示",
                ["health.boss"] = "ボスの体力を数値で表示",
                ["health.name_color"] = "味方の名前と同じ色",
                ["health.damage_log"] = "被ダメージログ（デバッグ）",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["health.enabled"] = "体力を数値で表示します。",
                ["health.dead"] = "死亡している間は数値を表示しません。",
                ["health.bar"] = "Anchor が HealthBar のとき、メニューや HUD 非表示などで体力バーが隠れる間は数値も隠します。",
                ["health.bold"] = "文字を太字にします。",
                ["health.color"] = "数値の文字の色です。不透明度は設定ファイルの値のままです。",
                ["health.shadow"] = "文字の後ろに影を付け、読みやすくします。",
                ["health.teammates"] = "味方 HUD の名前の下に現在の体力と最大体力を表示します。自分だけがこの Mod を入れれば使えます。",
                ["health.boss"] = "ボスの体力バーに数値を表示します。複数パーツのボスはバーから換算した概算値を表示し、参加側は Mod を導入したホストから最大値を取得します。",
                ["health.name_color"] = "味方の名前と同じ色で数値を表示します。オフの場合は Style.Color を使います。",
                ["health.damage_log"] = "受けた攻撃の元のダメージ、属性、継続ダメージ、攻撃元、実際に減った体力、ワンパンガード（即死を防ぐ仕組み）の状態を BepInEx のログに記録します。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
