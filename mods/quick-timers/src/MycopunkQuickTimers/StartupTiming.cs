using System;
using HarmonyLib;
using UnityEngine;

namespace MycopunkQuickTimers
{
    [HarmonyPatch]
    internal static class StartupTiming
    {
        private static float _firstCheck = -1f;
        private static bool _reported;

        internal static void Setup(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(StartupTiming)).Patch();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Startup timing unavailable: " + e.Message); }
        }

        [HarmonyPatch(typeof(Global), nameof(Global.BankCallback))]
        [HarmonyPostfix]
        private static void BankLoaded()
        {
            if (!_reported && _firstCheck >= 0f && StartMenu.Instance != null)
                BanksChecked(Global.AreAllBanksLoaded);
        }

        [HarmonyPatch(typeof(StartMenu), nameof(StartMenu.OnMusicLoaded))]
        [HarmonyPostfix]
        private static void MusicLoaded()
        {
            if (!_reported && StartMenu.Instance != null)
                Plugin.Log.LogInfo($"Startup timing: menu music ready at {Time.realtimeSinceStartup:F2} s since Unity startup.");
        }

        [HarmonyPatch(typeof(Global), nameof(Global.AreAllBanksLoaded), MethodType.Getter)]
        [HarmonyPostfix]
        private static void BanksChecked(bool __result)
        {
            if (_reported || StartMenu.Instance == null) return;
            float now = Time.realtimeSinceStartup;
            if (_firstCheck < 0f)
            {
                _firstCheck = now;
                Plugin.Log.LogInfo($"Startup timing: first required-bank check at {now:F2} s; ready={__result}.");
            }
            if (!__result) return;
            _reported = true;
            Plugin.Log.LogInfo($"Startup timing: required sound banks ready at {now:F2} s; waited {now - _firstCheck:F2} s after first check.");
        }
    }
}
