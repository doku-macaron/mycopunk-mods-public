using Pigeon.Movement;
using Unity.Netcode;

internal static class AttackerResolver
{
    public static Player Resolve(IDamageSource source)
    {
        Player player = source.GetBase() as Player;
        if (player != null) return player;
        Gun gun = source as Gun;
        if (gun != null && gun.Player != null) return gun.Player;
        Throwable throwable = source as Throwable;
        if (throwable != null && throwable.Player != null) return throwable.Player;

        NetworkBehaviour gear = source is IGear ? source as NetworkBehaviour : null;
        if (gear == null || !gear.IsSpawned || GameManager.players == null) return null;
        foreach (Player candidate in GameManager.players)
            if (candidate != null && candidate.OwnerClientId == gear.OwnerClientId) return candidate;
        return null;
    }
}
