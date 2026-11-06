using System;
using System.Globalization;

namespace MycopunkShared
{
    /// <summary>
    /// Values one mod hands to another at runtime. Each mod compiles its own copy of the shared code, so the values
    /// go through AppDomain data and use only mscorlib types (Func, string), which every assembly shares.
    /// A mod that is not installed simply leaves its slot empty.
    /// </summary>
    public static class ModChannels
    {
        /// <summary>
        /// Set by DPS Checker: <c>Func&lt;ulong, string&gt;</c>, Netcode client id → "max\tkills\testimated" (max DPS
        /// over a 1 s window, kills, estimated = 1 when DPS Checker guessed it from the damage RPCs because the
        /// player has no DPS Checker), invariant culture; null when nothing is known. Values reset at the start and
        /// end of each mission (MissionManager.OnMissionStarted_Client postfix, MissionManager.OnDestroy prefix).
        /// </summary>
        public const string DpsStatsKey = "MycopunkShared.DpsChecker.Stats.v1";

        public static void Publish(string key, object value)
        {
            try { AppDomain.CurrentDomain.SetData(key, value); } catch { }
        }

        public static T Get<T>(string key) where T : class
        {
            try { return AppDomain.CurrentDomain.GetData(key) as T; } catch { return null; }
        }

        public static string FormatDps(float max, int kills, bool estimated) =>
            max.ToString("R", CultureInfo.InvariantCulture) + "\t" + kills.ToString(CultureInfo.InvariantCulture) + "\t" + (estimated ? "1" : "0");

        public static bool TryParseDps(string text, out float max, out int kills, out bool estimated)
        {
            max = 0; kills = 0; estimated = false;
            if (string.IsNullOrEmpty(text)) return false;
            var p = text.Split('\t');
            if (p.Length < 3) return false;
            estimated = p[2] == "1";
            return float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out max) &&
                   int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out kills);
        }
    }
}
