using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MycopunkHealthNumbers
{
    internal sealed class BossHealthNetwork : IDisposable
    {
        private const string Request = "mycopunk.healthnumbers.boss.v1.request";
        private const string Response = "mycopunk.healthnumbers.boss.v1.response";
        private const float RequestInterval = 0.5f;
        private const float ValueLifetime = 3f;
        private readonly ManualLogSource _log;
        private readonly Dictionary<ulong, Entry> _values = new Dictionary<ulong, Entry>();
        private readonly List<ulong> _stale = new List<ulong>();
        private NetworkManager _manager;
        private bool _registered;
        private bool _serverHandler;
        private bool _failed;
        private ulong _nextNonce = unchecked((ulong)DateTime.UtcNow.Ticks);
        private float _nextCleanup;

        private sealed class Entry
        {
            public NetworkObject Object;
            public ulong Nonce;
            public float Max;
            public float Ratio;
            public float ReceivedAt = float.NegativeInfinity;
            public float NextRequest;
            public float LastUsed;
        }

        public BossHealthNetwork(ManualLogSource log) { _log = log; }

        public void Tick()
        {
            var current = NetworkManager.Singleton;
            if (current != _manager || (_manager != null && !_manager.IsListening))
            {
                Dispose();
                _manager = current != null && current.IsListening ? current : null;
                _failed = false;
            }
            if (_manager == null || _failed || !_manager.IsListening ||
                (!_manager.IsServer && !_manager.IsConnectedClient)) return;
            try
            {
                var messaging = _manager.CustomMessagingManager;
                if (messaging == null) return;
                if (!_registered)
                {
                    _serverHandler = _manager.IsServer;
                    if (_serverHandler) messaging.RegisterNamedMessageHandler(Request, OnRequest);
                    else messaging.RegisterNamedMessageHandler(Response, OnResponse);
                    _registered = true;
                    _log.LogInfo("Boss health sharing ready (" + (_serverHandler ? "host" : "client") + ").");
                }
                float now = Time.unscaledTime;
                if (now < _nextCleanup) return;
                _nextCleanup = now + 15f;
                foreach (var pair in _values)
                    if (pair.Value.Object == null || !pair.Value.Object.IsSpawned || now - pair.Value.LastUsed > 10f)
                        _stale.Add(pair.Key);
                foreach (ulong id in _stale) _values.Remove(id);
                _stale.Clear();
            }
            catch (Exception e)
            {
                _failed = true;
                _log.LogWarning("Boss health sharing unavailable; using local values: " + e.Message);
            }
        }

        public bool TryGet(EnemyBrain brain, out float max, out float ratio)
        {
            max = 0f;
            ratio = 0f;
            if (_manager == null || _failed || !_registered || _manager.IsServer || !_manager.IsConnectedClient || brain == null)
                return false;
            try
            {
                var obj = brain.NetworkObject;
                if (obj == null || !obj.IsSpawned) return false;
                ulong id = obj.NetworkObjectId;
                float now = Time.unscaledTime;
                if (!_values.TryGetValue(id, out var entry) || entry.Object != obj)
                    _values[id] = entry = new Entry { Object = obj };
                entry.LastUsed = now;
                if (now >= entry.NextRequest)
                {
                    entry.NextRequest = now + ValueLifetime;
                    entry.Nonce = ++_nextNonce;
                    using (var writer = new FastBufferWriter(16, Allocator.Temp))
                    {
                        writer.WriteValueSafe(id);
                        writer.WriteValueSafe(entry.Nonce);
                        _manager.CustomMessagingManager.SendNamedMessage(Request, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
                    }
                }
                if (now - entry.ReceivedAt >= ValueLifetime) return false;
                max = entry.Max;
                ratio = entry.Ratio;
                return max > 0f;
            }
            catch (Exception e)
            {
                _failed = true;
                _log.LogWarning("Boss health request failed; using percentage: " + e.Message);
                return false;
            }
        }

        private void OnRequest(ulong sender, FastBufferReader reader)
        {
            try
            {
                if (_manager == null || !_manager.IsServer || sender == _manager.LocalClientId ||
                    !_manager.ConnectedClients.ContainsKey(sender)) return;
                reader.ReadValueSafe(out ulong id);
                reader.ReadValueSafe(out ulong nonce);
                if (_manager.SpawnManager == null || !_manager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) ||
                    obj == null || !obj.IsSpawned) return;
                var brain = obj.GetComponent<EnemyBrain>();
                if (brain == null) brain = obj.GetComponentInChildren<EnemyBrain>(true);
                if (brain == null || brain.NetworkObject != obj) return;
                float max = brain.totalMaxHealth;
                if (max <= 0f || float.IsNaN(max) || float.IsInfinity(max)) return;
                var core = brain.Core;
                if (core == null || brain.EnemyClass == null) return;
                float ratio = core.NormalizedHealth;
                if (!brain.EnemyClass.config.onlyUseCoreHealthForHealthbar) ratio *= brain.NormalizedHealth;
                if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return;
                ratio = Mathf.Clamp01(ratio);
                using (var writer = new FastBufferWriter(24, Allocator.Temp))
                {
                    writer.WriteValueSafe(id);
                    writer.WriteValueSafe(nonce);
                    writer.WriteValueSafe(max);
                    writer.WriteValueSafe(ratio);
                    _manager.CustomMessagingManager.SendNamedMessage(Response, sender, writer, NetworkDelivery.Reliable);
                }
            }
            catch (Exception e) { _log.LogWarning("Invalid boss health request: " + e.Message); }
        }

        private void OnResponse(ulong sender, FastBufferReader reader)
        {
            try
            {
                if (_manager == null || _manager.IsServer || sender != NetworkManager.ServerClientId) return;
                reader.ReadValueSafe(out ulong id);
                reader.ReadValueSafe(out ulong nonce);
                reader.ReadValueSafe(out float max);
                reader.ReadValueSafe(out float ratio);
                if (max <= 0f || float.IsNaN(max) || float.IsInfinity(max) || ratio < 0f || ratio > 1f ||
                    float.IsNaN(ratio) || float.IsInfinity(ratio) ||
                    !_values.TryGetValue(id, out var entry) || entry.Nonce != nonce || entry.Object == null ||
                    !entry.Object.IsSpawned || _manager.SpawnManager == null ||
                    !_manager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) || obj != entry.Object) return;
                entry.Max = max;
                entry.Ratio = ratio;
                entry.ReceivedAt = Time.unscaledTime;
                entry.NextRequest = entry.ReceivedAt + RequestInterval;
            }
            catch (Exception e) { _log.LogWarning("Invalid boss health response: " + e.Message); }
        }

        public void Dispose()
        {
            try
            {
                if (_manager != null && _registered && _manager.CustomMessagingManager != null)
                {
                    if (_serverHandler) _manager.CustomMessagingManager.UnregisterNamedMessageHandler(Request);
                    else _manager.CustomMessagingManager.UnregisterNamedMessageHandler(Response);
                }
            }
            catch (Exception e) { _log.LogWarning("Boss health sharing cleanup failed: " + e.Message); }
            _registered = false;
            _values.Clear();
            _stale.Clear();
            _manager = null;
            _nextCleanup = 0f;
        }
    }
}
