using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Unity.Netcode;

internal sealed class DamageRpcHooks : IDisposable
{
    // shortcut: generated RPC IDs can change after a game update, verify their handlers before updating them.
    private const uint EnemyDamageRpcId = 3500258196;
    private const uint TrainingDamageRpcId = 4264469793;
    private const uint EnemyKillRpcId = 3954280513;

    private sealed class Hook
    {
        public NetworkBehaviour.RpcReceiveHandler Original;
        public NetworkBehaviour.RpcReceiveHandler Wrapper;
        public uint RpcId;
    }

    private readonly Dictionary<Type, Dictionary<uint, NetworkBehaviour.RpcReceiveHandler>> table;
    private readonly Dictionary<Type, Dictionary<uint, Hook>> hooks = new Dictionary<Type, Dictionary<uint, Hook>>();
    private readonly Action<NetworkBehaviour, FastBufferReader> observeDamage;
    private readonly Action<NetworkBehaviour, FastBufferReader> observeKill;
    private readonly ManualLogSource log;
    private bool installedLogged, skippedLogged, errorLogged;

    public DamageRpcHooks(ManualLogSource log, Action<NetworkBehaviour, FastBufferReader> observeDamage, Action<NetworkBehaviour, FastBufferReader> observeKill)
    {
        this.log = log;
        this.observeDamage = observeDamage;
        this.observeKill = observeKill;
        FieldInfo field = typeof(NetworkBehaviour).GetField("__rpc_func_table", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException("NetworkBehaviour.__rpc_func_table");
        table = field.GetValue(null) as Dictionary<Type, Dictionary<uint, NetworkBehaviour.RpcReceiveHandler>>;
        if (table == null) throw new InvalidOperationException("Unsupported RPC table type.");
    }

    public void InstallExisting()
    {
        foreach (Type type in table.Keys) Install(type);
    }

    public void Install(Type type)
    {
        try
        {
            bool enemy = typeof(EnemyPart).IsAssignableFrom(type);
            bool training = typeof(DynamicTrainingTarget).IsAssignableFrom(type);
            if (!enemy && !training) return;
            uint rpcId = enemy ? EnemyDamageRpcId : TrainingDamageRpcId;
            Type declaringType = enemy ? typeof(EnemyPart) : typeof(ObjectTarget);
            Dictionary<uint, NetworkBehaviour.RpcReceiveHandler> handlers;
            if (!table.TryGetValue(type, out handlers)) return;
            InstallHandler(type, handlers, rpcId, declaringType, observeDamage);
            if (typeof(EnemyCore).IsAssignableFrom(type)) InstallHandler(type, handlers, EnemyKillRpcId, typeof(EnemyPart), observeKill);
        }
        catch (Exception e)
        {
            if (!errorLogged) log.LogWarning("DPS estimate: could not observe an enemy RPC: " + e);
            errorLogged = true;
        }
    }

    private void InstallHandler(Type type, Dictionary<uint, NetworkBehaviour.RpcReceiveHandler> handlers, uint rpcId,
        Type declaringType, Action<NetworkBehaviour, FastBufferReader> observe)
    {
        Dictionary<uint, Hook> typeHooks;
        if (!hooks.TryGetValue(type, out typeHooks)) hooks[type] = typeHooks = new Dictionary<uint, Hook>();
        if (typeHooks.ContainsKey(rpcId)) return;
        NetworkBehaviour.RpcReceiveHandler original;
        if (!handlers.TryGetValue(rpcId, out original)) return;
        if (original == null || original.Method.DeclaringType != declaringType || original.Method.Name != "__rpc_handler_" + rpcId)
        {
            if (!skippedLogged) log.LogWarning("DPS estimate: unsupported or modified target RPC handler; estimation skipped for that handler.");
            skippedLogged = true;
            return;
        }
        NetworkBehaviour.RpcReceiveHandler wrapper = delegate(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
        {
            try { observe(target, reader); }
            catch (Exception e)
            {
                if (!errorLogged) log.LogWarning("DPS estimate: RPC observation failed: " + e);
                errorLogged = true;
            }
            original(target, reader, rpcParams);
        };
        typeHooks.Add(rpcId, new Hook { Original = original, Wrapper = wrapper, RpcId = rpcId });
        handlers[rpcId] = wrapper;
        if (!installedLogged) log.LogInfo("DPS estimate: registered target RPC observer.");
        installedLogged = true;
    }

    public void Dispose()
    {
        foreach (KeyValuePair<Type, Dictionary<uint, Hook>> item in hooks)
        {
            Dictionary<uint, NetworkBehaviour.RpcReceiveHandler> handlers;
            if (!table.TryGetValue(item.Key, out handlers)) continue;
            foreach (Hook hook in item.Value.Values)
            {
                NetworkBehaviour.RpcReceiveHandler current;
                if (handlers.TryGetValue(hook.RpcId, out current) && current == hook.Wrapper) handlers[hook.RpcId] = hook.Original;
            }
        }
        hooks.Clear();
    }
}
