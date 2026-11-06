/// <summary>
/// The text of a teammate's DPS line. No Unity: where the numbers come from is passed in, so it can be tested
/// on its own (the label passes Plugin.TryGetRemoteStats).
/// </summary>
internal sealed class TeammateDpsText
{
    internal delegate bool StatsSource(ulong clientId, out CombatStats stats, out bool dpsEstimated, out bool totalsEstimated);

    private readonly StatsSource source;
    private readonly System.Action estimateShown;
    private readonly StatsText text = new StatsText();

    /// <param name="source">Numbers for a client id; false when nothing is known (shown as estimated zeros).</param>
    /// <param name="estimateShown">Called whenever an estimated DPS is shown (the plugin logs the first one).</param>
    public TeammateDpsText(StatsSource source, System.Action estimateShown)
    {
        this.source = source;
        this.estimateShown = estimateShown;
    }

    public string Get(ulong clientId)
    {
        CombatStats stats;
        bool dpsEstimated, totalsEstimated;
        bool available = source(clientId, out stats, out dpsEstimated, out totalsEstimated);
        if (!available)
        {
            stats = default(CombatStats);
            dpsEstimated = totalsEstimated = true;
        }
        else if (dpsEstimated && estimateShown != null) estimateShown();
        return text.Get(stats, dpsEstimated, totalsEstimated);
    }
}
