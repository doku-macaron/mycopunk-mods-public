using System;
using System.Reflection;
using System.Collections.Generic;
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
        // The coroutine's state machine (MissionRewardsWindow.<ShowXPCoroutine>d__37) is compiler-generated, so C#
        // cannot name it or its fields; they are read by reflection, looked up once from the first instance.
        private static FieldInfo _ui, _xp, _level, _prevXP, _prevLevel, _time;
        private static int _fields; // 0: not looked up yet, 1: found, -1: missing (the postfix then does nothing)
        private const string LabelName = "XP numbers";
        private static bool _loggedLayout;
        private static readonly List<GameObject> Labels = new List<GameObject>();

        public static void Cleanup()
        {
            foreach (var label in Labels)
                if (label != null) UnityEngine.Object.Destroy(label);
            Labels.Clear();
        }

        private static bool FindFields(Type t)
        {
            _ui = AccessTools.Field(t, "ui");
            _xp = AccessTools.Field(t, "xp");
            _level = AccessTools.Field(t, "level");
            _prevXP = AccessTools.Field(t, "prevXP");
            _prevLevel = AccessTools.Field(t, "prevLevel");
            _time = AccessTools.Field(t, "<time>5__2");
            if (_ui == null || _xp == null || _level == null || _prevXP == null || _prevLevel == null || _time == null)
            {
                Plugin.Log.LogWarning("ShowXPCoroutine is not as expected; no XP numbers on the mission results.");
                return false;
            }
            return true;
        }

        /// <summary>Postfix on the MoveNext of the ShowXPCoroutine state machine.</summary>
        [HarmonyPatch(typeof(MissionRewardsWindow), nameof(MissionRewardsWindow.ShowXPCoroutine), MethodType.Enumerator)]
        internal static class ShowXPPatch
        {
            [HarmonyPostfix]
            private static void Postfix(object __instance)
            {
                if (_fields == 0)
                    _fields = FindFields(__instance.GetType()) ? 1 : -1;
                if (_fields < 0)
                    return;
                try
                {
                    var ui = _ui.GetValue(__instance) as ResourceUI;
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
                    var gearInfo = ui.GearInfo;
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
                    Func<int, int> costOf = n => data.GetLevelXPCost(n, false);
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
            Labels.Add(go);
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
