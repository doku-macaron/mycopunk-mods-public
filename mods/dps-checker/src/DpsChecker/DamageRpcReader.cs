using Unity.Collections;
using Unity.Netcode;

internal static class DamageRpcReader
{
    public static void Read(FastBufferReader original, out DamageData damage, out NetworkBehaviourReference source)
    {
        using (var copy = new FastBufferReader(original, Allocator.None, original.Length, original.Position))
        {
            copy.ReadValueSafe(out damage, default(FastBufferWriter.ForStructs));
            copy.ReadValueSafe(out source, default(FastBufferWriter.ForNetworkSerializable));
        }
    }

    public static NetworkBehaviourReference ReadKillSource(FastBufferReader original)
    {
        using (var copy = new FastBufferReader(original, Allocator.None, original.Length, original.Position))
        {
            NetworkBehaviourReference source;
            copy.ReadValueSafe(out source, default(FastBufferWriter.ForNetworkSerializable));
            return source;
        }
    }
}
