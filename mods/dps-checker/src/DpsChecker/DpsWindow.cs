using System.Collections.Generic;

internal sealed class DpsWindow
{
    private readonly Queue<KeyValuePair<float, float>> hits = new Queue<KeyValuePair<float, float>>();
    private float total;
    private double burstDamage;
    private float lastBurstTime;
    private bool burstActive;
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

    public void AddBurst(float time, float damage)
    {
        if (damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
        if (!burstActive || time - lastBurstTime >= 4.5f) burstDamage = 0;
        burstDamage += damage;
        lastBurstTime = time;
        burstActive = true;
    }

    public CombatStats Snapshot(float time)
    {
        return new CombatStats { Dps = Current(time), Max = Peak, TotalDamage = TotalDamage, Kills = Kills, TotalsKnown = true, DamageKnown = true };
    }

    public double Burst(float time, out float remaining)
    {
        remaining = burstActive ? 4.5f - (time - lastBurstTime) : 0f;
        if (remaining <= 0f) { burstDamage = 0; burstActive = false; return 0; }
        return burstDamage;
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
        burstDamage = 0;
        lastBurstTime = 0;
        burstActive = false;
    }
}
