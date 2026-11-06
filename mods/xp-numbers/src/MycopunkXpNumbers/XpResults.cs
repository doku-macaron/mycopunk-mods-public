using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace MycopunkXpNumbers
{
    /// <summary>
    /// "levelXP / next level cost XP" under each level bar of the mission results ("XP" page of
    /// MissionRewardsWindow), following the bar while it fills.
    ///
    /// The game fills each bar in MissionRewardsWindow.ShowXPCoroutine(ui, delay, xp, level, prevXP, prevLevel):
    /// after `delay` it shows `ui` (a ResourceUI), then for 2.2 s eases a fractional level
    ///   v = Lerp(prevLevel + prevXP / cost(prevLevel + 1), level + xp / cost(level + 1), EaseOutQuartic(time))
    /// with cost(n) = GearData.GetLevelXPCost(n, false), and sets the bar (ui child 3, its child 0) to the
    /// x scale v - floor(v) and the level text (ui child 4) to floor(v). A postfix on the coroutine's MoveNext
    /// recomputes the same v from its fields (<see cref="XpText.AtFractionalLevel"/>) and writes the numbers into a
    /// label of its own, a copy of the level text placed under the right end of the bar. Read only.
    /// </summary>
    internal static class XpResults
    {
        private static FieldInfo _ui, _xp, _level, _prevXP, _prevLevel, _time;
        private static Func<PlayerData.GearData, int, bool, int> _cost;
        private const string LabelName = "XP numbers";
        private static bool _loggedLayout;

        public static void Install(Harmony harmony)
        {
            var start = AccessTools.Method(typeof(MissionRewardsWindow), "ShowXPCoroutine");
            var moveNext = start != null ? AccessTools.EnumeratorMoveNext(start) : null;
            if (moveNext == null)
            {
                Plugin.Log.LogWarning("MissionRewardsWindow.ShowXPCoroutine not found; no XP numbers on the mission results.");
                return;
            }
            var t = moveNext.DeclaringType;
            _ui = AccessTools.Field(t, "ui");
            _xp = AccessTools.Field(t, "xp");
            _level = AccessTools.Field(t, "level");
            _prevXP = AccessTools.Field(t, "prevXP");
            _prevLevel = AccessTools.Field(t, "prevLevel");
            _time = AccessTools.Field(t, "<time>5__2");
            var cost = AccessTools.Method(typeof(PlayerData.GearData), "GetLevelXPCost", new[] { typeof(int), typeof(bool) });
            if (_ui == null || _xp == null || _level == null || _prevXP == null || _prevLevel == null || _time == null || cost == null)
            {
                Plugin.Log.LogWarning("ShowXPCoroutine is not as expected; no XP numbers on the mission results.");
                return;
            }
            _cost = AccessTools.MethodDelegate<Func<PlayerData.GearData, int, bool, int>>(cost);
            harmony.Patch(moveNext, postfix: new HarmonyMethod(typeof(XpResults), nameof(MoveNext_Postfix)));
        }

        private static void MoveNext_Postfix(object __instance)
        {
            try
            {
                var ui = _ui.GetValue(__instance) as Component;
                if (ui == null || !ui.gameObject.activeInHierarchy || ui.transform.childCount < 5)
                    return; // still waiting for its delay (the time field counts the delay then)
                var label = Label(ui.transform);
                if (label == null)
                    return;
                if (!Plugin.Enabled.Value)
                {
                    label.text = "";
                    return;
                }
                var gearInfo = AccessTools.Property(ui.GetType(), "GearInfo")?.GetValue(ui) as GearInfo;
                var data = gearInfo != null ? PlayerData.GetGearData(gearInfo) : null;
                if (data == null)
                {
                    label.text = "";
                    return;
                }
                int prevLevel = Convert.ToInt32(_prevLevel.GetValue(__instance));
                int level = Convert.ToInt32(_level.GetValue(__instance));
                float prevXP = Convert.ToSingle(_prevXP.GetValue(__instance));
                float xp = Convert.ToSingle(_xp.GetValue(__instance));
                float time = Mathf.Clamp01(Convert.ToSingle(_time.GetValue(__instance)));
                Func<int, int> costOf = n => _cost(data, n, false);
                float from = prevLevel + prevXP / costOf(prevLevel + 1);
                float to = level + xp / costOf(level + 1);
                float v = Mathf.LerpUnclamped(from, to, XpText.EaseOutQuartic(time));
                XpText.AtFractionalLevel(v, costOf, out int shownLevel, out int shownXP, out int shownCost);
                label.text = XpText.Progress(shownXP, shownCost, shownLevel, data.MaxLevel);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("XP numbers on the mission results failed: " + e);
            }
        }

        /// <summary>Our label in the row: a copy of the level text (child 4), right-aligned under the bar (child 3).</summary>
        private static TextMeshProUGUI Label(Transform row)
        {
            var bar = row.GetChild(3) as RectTransform;
            if (bar == null)
                return null;
            var existing = bar.Find(LabelName);
            if (existing != null)
                return existing.GetComponent<TextMeshProUGUI>();
            var template = row.GetChild(4).GetComponent<TextMeshProUGUI>();
            if (template == null)
                return null;
            if (!_loggedLayout)
            {
                _loggedLayout = true;
                Plugin.Log.LogInfo("results row " + row.name + ": bar " + bar.name + " " + bar.rect.size +
                    ", level text " + template.name + " size " + template.fontSize + ", align " + template.alignment);
            }
            var go = UnityEngine.Object.Instantiate(template.gameObject, bar, false);
            go.name = LabelName;
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.TopRight;
            label.fontSize = template.fontSize * Plugin.ResultsFontScale.Value;
            label.text = "";
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 1f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(400f, label.fontSize * 1.4f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            return label;
        }
    }
}
