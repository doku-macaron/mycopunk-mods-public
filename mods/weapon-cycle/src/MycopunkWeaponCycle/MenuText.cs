using System.Collections.Generic;
using MycopunkShared;

namespace MycopunkWeaponCycle
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
                ["cycle.heavy"] = "Heavy weapon in the weapon cycle",
                ["cycle.skip_empty"] = "Wheel skips weapons out of ammo",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["cycle.heavy"] = "武器の切り替えに重量武器を含める",
                ["cycle.skip_empty"] = "ホイールで弾切れの武器を飛ばす",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["cycle.heavy"] = "しまってある重量武器を武器の切り替えに含めます。ホイールは 武器 1 → 武器 2 → 重量武器 の順に回り、武器 1 キーも同じ順に切り替えます。武器 2 キーはそのままです。この変更は即時に反映されます。",
                ["cycle.skip_empty"] = "ホイールで切り替えるとき、弾倉と予備の弾がどちらも 0 の武器を飛ばします。弾が無くても撃てる武器は飛ばしません。ほかの武器が全部弾切れのときは、切り替えずに今の武器のままにします。武器のキーはそのままです。この変更は即時に反映されます。",
            },
        };

        public static object[] Label(string key) => ModsMenuTag.Label(key, Names, Descriptions);
    }
}
