using System;
using HarmonyLib;
using UnityEngine;

namespace MycopunkQuickTimers
{
    internal static class StartupTiming
    {
        private static float _firstCheck = -1f;
        private static bool _reported;

        internal static void Setup(Harmony harmony)
        {
            try
            {
                var music = AccessTools.Method(typeof(StartMenu), "OnMusicLoaded", Type.EmptyTypes);
                var banks = AccessTools.PropertyGetter(typeof(Global), "AreAllBanksLoaded");
                var bankCallback = AccessTools.Method(typeof(Global), "BankCallback");
                if (music != null) harmony.Patch(music, postfix: new HarmonyMethod(typeof(StartupTiming), nameof(MusicLoaded)));
                if (banks != null) harmony.Patch(banks, postfix: new HarmonyMethod(typeof(StartupTiming), nameof(BanksChecked)));
                if (bankCallback != null) harmony.Patch(bankCallback, postfix: new HarmonyMethod(typeof(StartupTiming), nameof(BankLoaded)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Startup timing unavailable: " + e.Message); }
        }

        private static void BankLoaded()
        {
            if (!_reported && _firstCheck >= 0f && StartMenu.Instance != null)
                BanksChecked(Global.AreAllBanksLoaded);
        }

        private static void MusicLoaded()
        {
            if (!_reported && StartMenu.Instance != null)
                Plugin.Log.LogInfo($"Startup timing: menu music ready at {Time.realtimeSinceStartup:F2} s since Unity startup.");
        }

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
