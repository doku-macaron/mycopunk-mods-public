using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

internal sealed class DpsNetwork : IDisposable
{
    private const string Message = "mycopunk.dpschecker.v1";
    private readonly ManualLogSource log;
    private readonly RemoteDps values = new RemoteDps();
    private readonly HashSet<ulong> peers = new HashSet<ulong>();
    private NetworkManager manager;
    private float nextSend;
    private bool failed;

    public DpsNetwork(ManualLogSource log) { this.log = log; }
    public bool TryGet(ulong id, out CombatStats value) { return values.TryGet(id, Time.unscaledTime, out value); }
    public void ClearValues() { values.Clear(); nextSend = 0; }

    public void Tick(CombatStats localStats)
    {
        NetworkManager current = NetworkManager.Singleton;
        if (current != manager || (manager != null && !manager.IsListening))
        {
            Dispose();
            manager = current != null && current.IsListening ? current : null;
            failed = false;
        }
        if (manager == null || failed || !manager.IsListening || (!manager.IsServer && !manager.IsConnectedClient)) return;
        try
        {
            if (manager.CustomMessagingManager == null) return;
            if (!registered)
            {
                manager.CustomMessagingManager.RegisterNamedMessageHandler(Message, OnMessage);
                manager.OnClientDisconnectCallback += OnDisconnect;
                registered = true;
            }
            if (Time.unscaledTime < nextSend) return;
            if (!manager.IsServer)
            {
                CombatStats hostValue;
                nextSend = Time.unscaledTime + (values.TryGet(NetworkManager.ServerClientId, Time.unscaledTime, out hostValue) ? 0.25f : 1f);
                Send(NetworkManager.ServerClientId, manager.LocalClientId, localStats);
                return;
            }
            nextSend = Time.unscaledTime + 0.25f;
            foreach (ulong peer in peers)
            {
                Send(peer, manager.LocalClientId, localStats);
                foreach (ulong source in peers)
                {
                    CombatStats value;
                    if (source != peer && values.TryGet(source, Time.unscaledTime, out value)) Send(peer, source, value);
                }
            }
        }
        catch (Exception e)
        {
            log.LogWarning("DPS sharing disabled for this session: " + e);
            failed = true;
        }
    }

    private bool registered;

    private void Send(ulong recipient, ulong source, CombatStats value)
    {
        using (var writer = new FastBufferWriter(26, Allocator.Temp))
        {
            writer.WriteValueSafe(source);
            writer.WriteValueSafe(value.Dps);
            writer.WriteValueSafe(value.Max);
            writer.WriteValueSafe(value.Kills);
            writer.WriteValueSafe((byte)(value.TotalsKnown ? 1 : 0));
            writer.WriteValueSafe(value.TotalDamage);
            writer.WriteValueSafe((byte)(value.DamageKnown ? 1 : 0));
            manager.CustomMessagingManager.SendNamedMessage(Message, recipient, writer, NetworkDelivery.UnreliableSequenced);
        }
    }

    private void OnMessage(ulong sender, FastBufferReader reader)
    {
        try
        {
            ulong source;
            CombatStats value = default(CombatStats);
            reader.ReadValueSafe(out source);
            reader.ReadValueSafe(out value.Dps);
            if (reader.Length - reader.Position >= 9)
            {
                reader.ReadValueSafe(out value.Max);
                reader.ReadValueSafe(out value.Kills);
                byte totalsKnown;
                reader.ReadValueSafe(out totalsKnown);
                if (totalsKnown > 1) return;
                value.TotalsKnown = totalsKnown == 1;
            }
            if (reader.Length - reader.Position >= 5)
            {
                reader.ReadValueSafe(out value.TotalDamage);
                byte damageKnown;
                reader.ReadValueSafe(out damageKnown);
                if (damageKnown > 1) return;
                value.DamageKnown = damageKnown == 1;
            }
            if (manager.IsServer)
            {
                source = sender;
                if (sender == manager.LocalClientId || !manager.ConnectedClients.ContainsKey(sender)) return;
                if (values.Set(source, value, Time.unscaledTime)) peers.Add(sender);
            }
            else if (sender == NetworkManager.ServerClientId && source != manager.LocalClientId)
                values.Set(source, value, Time.unscaledTime);
        }
        catch (Exception e) { log.LogWarning("Invalid DPS message: " + e.Message); }
    }

    private void OnDisconnect(ulong id)
    {
        peers.Remove(id);
        values.Remove(id);
    }

    public void Dispose()
    {
        if (manager != null && registered)
        {
            manager.OnClientDisconnectCallback -= OnDisconnect;
            if (manager.CustomMessagingManager != null) manager.CustomMessagingManager.UnregisterNamedMessageHandler(Message);
        }
        registered = false;
        peers.Clear();
        values.Clear();
        manager = null;
        nextSend = 0;
    }
}
