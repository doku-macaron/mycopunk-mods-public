using System.Globalization;

internal struct CombatStats
{
    public float Dps;
    public float Max;
    public float TotalDamage;
    public int Kills;
    public bool TotalsKnown;
    public bool DamageKnown;

    public static string Compact(float value)
    {
        if (value < 1000f) return value.ToString("0", CultureInfo.InvariantCulture);
        if (value < 999950f) return (value / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return (value / 1000000f).ToString("0.#", CultureInfo.InvariantCulture) + "m";
    }

    public string Format(bool dpsEstimated, bool totalsEstimated, bool local = false)
    {
        string dpsPrefix = dpsEstimated ? "~" : "";
        string totalsPrefix = totalsEstimated ? "~" : "";
        string killsPrefix = totalsEstimated && (dpsEstimated || !TotalsKnown) ? "~" : "";
        return "DPS " + dpsPrefix + Compact(Dps) + "  T " + totalsPrefix + Compact(TotalDamage) + "  K " + killsPrefix + Kills;
    }
}

/// <summary>
/// Remembers the last text so that it is only rebuilt when the numbers change. The labels are refreshed
/// every frame (or every OnGUI event), and building the same string each time only makes garbage.
/// The exact values are compared, so the text is never stale; outside combat they do not change at all.
/// </summary>
internal sealed class StatsText
{
    private string text;
    private float dps, totalDamage;
    private int kills;
    private int flags = -1;

    public string Get(CombatStats stats, bool dpsEstimated, bool totalsEstimated, bool local = false)
    {
        int f = (dpsEstimated ? 1 : 0) | (totalsEstimated ? 2 : 0) | (local ? 4 : 0);
        if (text == null || !stats.Dps.Equals(dps) || !stats.TotalDamage.Equals(totalDamage) || stats.Kills != kills || f != flags)
        {
            text = stats.Format(dpsEstimated, totalsEstimated, local);
            dps = stats.Dps;
            totalDamage = stats.TotalDamage;
            kills = stats.Kills;
            flags = f;
        }
        return text;
    }
}
