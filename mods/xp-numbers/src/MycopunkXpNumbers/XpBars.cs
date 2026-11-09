using HarmonyLib;
using Pigeon.Movement;
using TMPro;

namespace MycopunkXpNumbers
{
    /// <summary>
    /// Adds "levelXP / next level cost XP" to the game's two level bars, right after the game fills them:
    ///  - GearDetailsWindow.UpdateXPBar (the build screen of a weapon, gear or class: "レベル23" under the bar).
    ///    The numbers go after the level, a little smaller.
    ///  - GearSelectionWindow.UpdateXPBar (the screen right after TAB: the class name, the bar and the level "22" at
    ///    its right). The level text is right-aligned, so the numbers go in front of it.
    /// Both read the same GearData the game used (PlayerData.GetGearData of the window's prefab, or of the local
    /// player's Character). Read only: nothing in the save changes.
    /// </summary>
    internal static class XpBars
    {
        [HarmonyPatch(typeof(GearDetailsWindow), nameof(GearDetailsWindow.UpdateXPBar))]
        internal static class DetailsPatch
        {
            [HarmonyPostfix]
            private static void Postfix(GearDetailsWindow __instance)
            {
                try
                {
                    if (!Plugin.Enabled.Value)
                        return;
                    var prefab = __instance.UpgradablePrefab;
                    var text = __instance.levelText;
                    string xp = prefab != null ? Progress(PlayerData.GetGearData(prefab)) : "";
                    if (text != null && xp.Length > 0)
                        text.text = text.text + "  <size=80%>" + xp + "</size>";
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("XP numbers on the build screen failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(GearSelectionWindow), nameof(GearSelectionWindow.UpdateXPBar))]
        internal static class SelectionPatch
        {
            [HarmonyPostfix]
            private static void Postfix(GearSelectionWindow __instance)
            {
                try
                {
                    if (!Plugin.Enabled.Value)
                        return;
                    var player = Player.LocalPlayer;
                    var character = player != null ? player.Character : null;
                    var text = __instance.levelText;
                    string xp = character != null ? Progress(PlayerData.GetGearData(character)) : "";
                    if (text != null && xp.Length > 0)
                        text.text = "<size=70%>" + xp + "</size>  " + text.text;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("XP numbers on the class screen failed: " + e);
                }
            }
        }

        private static string Progress(PlayerData.GearData data) =>
            data == null ? "" : XpText.Progress(data.LevelXP, data.NextLevelXPCost, data.Level, data.MaxLevel);
    }
}
