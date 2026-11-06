using System.Collections.Generic;

internal sealed class RemoteDps
{
    private struct Sample { public CombatStats Value; public float Time; }
    private readonly Dictionary<ulong, Sample> values = new Dictionary<ulong, Sample>();

    public bool Set(ulong id, CombatStats value, float time)
    {
        if (value.Dps < 0 || float.IsNaN(value.Dps) || float.IsInfinity(value.Dps) ||
            value.Max < 0 || float.IsNaN(value.Max) || float.IsInfinity(value.Max) ||
            value.TotalDamage < 0 || float.IsNaN(value.TotalDamage) || float.IsInfinity(value.TotalDamage) || value.Kills < 0) return false;
        Sample old;
        if (!value.TotalsKnown && values.TryGetValue(id, out old)) value.Max = System.Math.Max(value.Max, System.Math.Max(old.Value.Max, value.Dps));
        else if (!value.TotalsKnown) value.Max = System.Math.Max(value.Max, value.Dps);
        values[id] = new Sample { Value = value, Time = time };
        return true;
    }

    public bool TryGet(ulong id, float time, out CombatStats value)
    {
        Sample sample;
        if (values.TryGetValue(id, out sample) && time - sample.Time < 3f)
        {
            value = sample.Value;
            return true;
        }
        value = default(CombatStats);
        return false;
    }

    public void Remove(ulong id) { values.Remove(id); }
    public void Clear() { values.Clear(); }
}
