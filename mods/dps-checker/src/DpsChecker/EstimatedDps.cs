using System.Collections.Generic;

internal sealed class EstimatedDps
{
    private sealed class PlayerDamage
    {
        public readonly DpsWindow Window = new DpsWindow();
    }

    private readonly Dictionary<ulong, PlayerDamage> players = new Dictionary<ulong, PlayerDamage>();

    public void Add(ulong id, float time, float damage)
    {
        if (damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
        PlayerDamage player;
        if (!players.TryGetValue(id, out player)) players[id] = player = new PlayerDamage();
        player.Window.Add(time, damage);
    }

    public void AddKill(ulong id)
    {
        PlayerDamage player;
        if (!players.TryGetValue(id, out player)) players[id] = player = new PlayerDamage();
        player.Window.AddKill();
    }

    public bool TryGet(ulong id, float time, out CombatStats stats)
    {
        PlayerDamage player;
        if (players.TryGetValue(id, out player))
        {
            stats = player.Window.Snapshot(time);
            return true;
        }
        stats = default(CombatStats);
        return false;
    }

    public void Clear() { players.Clear(); }
}
