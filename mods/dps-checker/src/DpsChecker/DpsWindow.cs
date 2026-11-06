using System.Collections.Generic;

internal sealed class DpsWindow
{
    private readonly Queue<KeyValuePair<float, float>> hits = new Queue<KeyValuePair<float, float>>();
    private float total;
    public float Peak { get; private set; }
    public float TotalDamage { get; private set; }
    public int Kills { get; private set; }

    public void Add(float time, float damage)
    {
        if (damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
        Current(time);
        hits.Enqueue(new KeyValuePair<float, float>(time, damage));
        total += damage;
        TotalDamage += damage;
        if (total > Peak) Peak = total;
    }

    public void AddKill() { Kills++; }

    public CombatStats Snapshot(float time)
    {
        return new CombatStats { Dps = Current(time), Max = Peak, TotalDamage = TotalDamage, Kills = Kills, TotalsKnown = true, DamageKnown = true };
    }

    public float Current(float time)
    {
        while (hits.Count > 0 && time - hits.Peek().Key >= 1f)
            total -= hits.Dequeue().Value;
        if (hits.Count == 0) total = 0;
        return total;
    }

    public void Clear()
    {
        hits.Clear();
        total = 0;
        Peak = 0;
        TotalDamage = 0;
        Kills = 0;
    }
}
