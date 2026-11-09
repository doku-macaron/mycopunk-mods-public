using BepInEx;
using HarmonyLib;
using Pigeon.Movement;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.2.2")]

[BepInPlugin("f1w3.mycopunk.dpschecker", "DPS Checker", "0.2.2")]

[BepInDependency(MycopunkShared.MacaronCore.Guid, MycopunkShared.MacaronCore.Version)]
[BepInProcess("Mycopunk.exe")]
[MycoMod(null, ModFlags.IsClientSide)]
public sealed class Plugin : BaseUnityPlugin
{
    private static readonly DpsWindow Window = new DpsWindow();
    private static readonly System.Collections.Generic.HashSet<EnemyCore> EstimatedKills = new System.Collections.Generic.HashSet<EnemyCore>();
    private static readonly EstimatedDps Estimates = new EstimatedDps();
    private readonly LocalDpsLabel localLabel = new LocalDpsLabel();
    private readonly BurstLabel burstLabel = new BurstLabel();
    private readonly StatsText fallbackText = new StatsText();
    private Harmony harmony;
    private bool ready;
    private bool localHudFailed;
    private bool burstHudFailed;
    private bool missionHooksReady;
    private DpsNetwork network;
    private DamageRpcHooks damageRpcHooks;
    private static bool estimateErrorLogged;
    private static bool estimateRpcLogged, estimateEventLogged, estimateMissingSourceLogged, estimateUnknownSourceLogged, estimateSuccessLogged;
    private bool estimateDisplayLogged;
    internal static Plugin Instance { get; private set; }

    private void Awake()
    {
        try
        {
            harmony = new Harmony("mycopunk.dpschecker");
            harmony.CreateClassProcessor(typeof(LocalDamagePatch)).Patch();
            harmony.CreateClassProcessor(typeof(LocalKillPatch)).Patch();
            SceneManager.sceneLoaded += OnSceneLoaded;
            network = new DpsNetwork(Logger);
            Instance = this;
            MycopunkShared.ModChannels.Publish(MycopunkShared.ModChannels.DpsStatsKey, new System.Func<ulong, string>(StatsFor));
            ready = true;
            MycopunkShared.Hotkeys.ReportConflicts(this);
            Logger.LogInfo("DPS Checker loaded.");
        }
        catch (System.Exception e)
        {
            Logger.LogError("DPS Checker could not hook damage events: " + e);
            if (harmony != null) harmony.UnpatchSelf();
            return;
        }
        try
        {
            harmony.CreateClassProcessor(typeof(TeammateInitializePatch)).Patch();
            foreach (var hud in FindObjectsByType<TeammateHUD>(FindObjectsInactive.Include))
                if (hud.player is Player player) OnTeammateInitialize(hud, player);
        }
        catch (System.Exception e) { Logger.LogWarning("DPS teammate HUD could not be hooked: " + e); }
        try
        {
            harmony.CreateClassProcessor(typeof(BurstDamagePatch)).Patch();
        }
        catch (System.Exception e) { Logger.LogWarning("DPS burst damage could not be hooked: " + e); }
        try
        {
            harmony.CreateClassProcessor(typeof(MissionStartedPatch)).Patch();
            harmony.CreateClassProcessor(typeof(MissionDestroyPatch)).Patch();
            missionHooksReady = true;
        }
        catch (System.Exception e) { Logger.LogWarning("DPS mission reset hooks could not be installed: " + e); }
        try
        {
            harmony.CreateClassProcessor(typeof(EnemyDamageServerPatch)).Patch();
            harmony.CreateClassProcessor(typeof(ObjectDamageServerPatch)).Patch();
            harmony.CreateClassProcessor(typeof(EnemyKillServerPatch)).Patch();
            damageRpcHooks = new DamageRpcHooks(Logger, OnEnemyDamageRpc, OnEnemyKillRpc);
            harmony.CreateClassProcessor(typeof(NetworkVariablesPatch)).Patch();
            damageRpcHooks.InstallExisting();
            Logger.LogInfo("DPS estimate: damage hooks installed.");
        }
        catch (System.Exception e) { Logger.LogWarning("DPS estimation could not be hooked: " + e); }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        try { if (harmony != null) harmony.UnpatchSelf(); }
        catch (System.Exception e) { Logger.LogWarning("DPS hook cleanup failed: " + e); }
        try { if (damageRpcHooks != null) damageRpcHooks.Dispose(); }
        catch (System.Exception e) { Logger.LogWarning("DPS RPC cleanup failed: " + e); }
        try { if (network != null) network.Dispose(); }
        catch (System.Exception e) { Logger.LogWarning("DPS network cleanup failed: " + e); }
        localLabel.Clear();
        burstLabel.Clear();
        Instance = null;
        MycopunkShared.ModChannels.Publish(MycopunkShared.ModChannels.DpsStatsKey, null);
        foreach (TeammateDpsLabel label in FindObjectsByType<TeammateDpsLabel>(FindObjectsInactive.Include)) Destroy(label);
        Window.Clear();
        Estimates.Clear();
        EstimatedKills.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!missionHooksReady) ResetStats();
        localLabel.Clear();
        burstLabel.Clear();
        localHudFailed = false;
        burstHudFailed = false;
    }

    private static void OnMissionBoundary(MissionManager __instance)
    {
        if (Instance == null || __instance != MissionManager.Instance) return;
        try { Instance.ResetStats(); }
        catch (System.Exception e) { Instance.Logger.LogWarning("DPS mission reset failed: " + e); }
    }

    private void ResetStats()
    {
        Window.Clear();
        Estimates.Clear();
        EstimatedKills.Clear();
        if (network != null) network.ClearValues();
    }

    private static void OnDamage(ref DamageCallbackData data)
    {
        float damage = data.damageData.damage;
        if (data.target == null || ((data.target.Type & TargetType.Enemy) == 0 && !(data.target is DynamicTrainingTarget)) ||
            damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
        Window.Add(Time.unscaledTime, damage);
    }

    private static void OnBurstDamage(ref DamageCallbackData data)
    {
        if (Instance == null || !Instance.ready) return;
        float damage = data.damageData.damage;
        if (damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
        try
        {
            if (ReferenceEquals(data.target, Player.LocalPlayer) && data.source != null &&
                ReferenceEquals(data.source.GetBase(), data.target)) return;
            Window.AddBurst(Time.unscaledTime, damage);
        }
        catch (System.Exception e) { Instance.Logger.LogWarning("DPS burst damage could not be recorded: " + e.Message); }
    }

    private void Update()
    {
        if (!ready) return;
        network.Tick(Window.Snapshot(Time.unscaledTime));
    }

    private void LateUpdate()
    {
        if (!ready) return;
        if (!burstHudFailed)
        {
            try
            {
                var look = PlayerLook.Instance;
                float remaining;
                double damage = Window.Burst(Time.unscaledTime, out remaining);
                burstLabel.Refresh(look != null ? look.PlayerTitleText : null, damage, remaining);
            }
            catch (System.Exception e)
            {
                burstHudFailed = true;
                burstLabel.Clear();
                Logger.LogWarning("DPS burst HUD could not be created: " + e);
            }
        }
        if (localHudFailed) return;
        try
        {
            PlayerLook look = PlayerLook.Instance;
            Player player = Player.LocalPlayer;
            var gearHuds = player != null ? player.gearHUDs : null;
            localLabel.Refresh(look != null ? look.PlayerTitleText : null, gearHuds, Window.Snapshot(Time.unscaledTime));
        }
        catch (System.Exception e)
        {
            localHudFailed = true;
            localLabel.Clear();
            Logger.LogWarning("DPS local HUD could not be created: " + e);
        }
    }

    private static void OnLocalKill(ref KillCallbackData data)
    {
        if (data.target is EnemyCore) Window.AddKill();
    }

    // For other mods (MycopunkShared.ModChannels.DpsStatsKey), e.g. the mission log.
    private static string StatsFor(ulong clientId)
    {
        try
        {
            if (Instance == null) return null;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && clientId == manager.LocalClientId)
            {
                CombatStats local = Window.Snapshot(Time.unscaledTime);
                return MycopunkShared.ModChannels.FormatDps(local.Max, local.Kills, false);
            }
            CombatStats stats;
            bool dpsEstimated, totalsEstimated;
            if (!Instance.TryGetRemoteStats(clientId, out stats, out dpsEstimated, out totalsEstimated)) return null;
            return MycopunkShared.ModChannels.FormatDps(stats.Max, stats.Kills, dpsEstimated || !stats.TotalsKnown);
        }
        catch { return null; }
    }

    internal bool TryGetRemoteStats(ulong clientId, out CombatStats stats, out bool dpsEstimated, out bool totalsEstimated)
    {
        CombatStats observed;
        bool hasObserved = Estimates.TryGet(clientId, Time.unscaledTime, out observed);
        if (network.TryGet(clientId, out stats))
        {
            dpsEstimated = false;
            totalsEstimated = !stats.TotalsKnown || !stats.DamageKnown;
            if (totalsEstimated && hasObserved)
            {
                stats.Max = System.Math.Max(stats.Max, observed.Max);
                if (!stats.TotalsKnown) stats.Kills = observed.Kills;
                if (!stats.DamageKnown) stats.TotalDamage = observed.TotalDamage;
            }
            return true;
        }
        dpsEstimated = true;
        totalsEstimated = true;
        stats = observed;
        return hasObserved;
    }

    private static void OnEnemyDamageServer(EnemyPart __instance, DamageData data, IDamageSource source)
    {
        RecordEstimate(__instance, data, source, false);
    }

    private static void OnObjectDamageServer(ObjectTarget __instance, DamageData data, IDamageSource source)
    {
        if (__instance is DynamicTrainingTarget) RecordEstimate(__instance, data, source, false);
    }

    private static void OnEnemyKillServer(EnemyPart __instance, IDamageSource source)
    {
        EnemyCore core = __instance as EnemyCore;
        if (core != null && core.IsAlive) RecordEstimatedKill(core, source);
    }

    private static void OnEnemyKillRpc(NetworkBehaviour target, FastBufferReader reader)
    {
        try
        {
            EnemyCore core = target as EnemyCore;
            if (core == null || core.IsServer || !core.IsClient) return;
            NetworkBehaviourReference sourceRef = DamageRpcReader.ReadKillSource(reader);
            NetworkBehaviour source;
            RecordEstimatedKill(core, sourceRef.TryGet(out source) ? source as IDamageSource : null);
        }
        catch (System.Exception e) { LogEstimateError(e); }
    }

    private static void RecordEstimatedKill(EnemyCore core, IDamageSource source)
    {
        try
        {
            if (Instance == null || source == null || EstimatedKills.Contains(core)) return;
            Player player = AttackerResolver.Resolve(source);
            if (player == null || player.IsLocalPlayer) return;
            EstimatedKills.Add(core);
            Estimates.AddKill(player.OwnerClientId);
        }
        catch (System.Exception e) { LogEstimateError(e); }
    }

    private static void OnNetworkVariablesInitialized(NetworkBehaviour __instance)
    {
        if (Instance != null && Instance.damageRpcHooks != null && (__instance is EnemyPart || __instance is DynamicTrainingTarget))
            Instance.damageRpcHooks.Install(__instance.GetType());
    }

    private static void OnEnemyDamageRpc(NetworkBehaviour __0, FastBufferReader __1)
    {
        try
        {
            if (!(__0 is EnemyPart) && !(__0 is DynamicTrainingTarget)) return;
            if (__0.IsServer || !__0.IsClient) return;
            if (!estimateRpcLogged && Instance != null)
            {
                estimateRpcLogged = true;
                Instance.Logger.LogInfo("DPS estimate: received a target damage RPC.");
            }
            DamageData data;
            NetworkBehaviourReference sourceRef;
            DamageRpcReader.Read(__1, out data, out sourceRef);
            NetworkBehaviour sourceObject;
            IDamageSource source = sourceRef.TryGet(out sourceObject) ? sourceObject as IDamageSource : null;
            RecordEstimate(__0, data, source, true);
        }
        catch (System.Exception e) { LogEstimateError(e); }
    }

    private static void RecordEstimate(NetworkBehaviour target, DamageData data, IDamageSource source, bool client)
    {
        try
        {
            ITarget damageTarget = target as ITarget;
            if (Instance == null || target == null || damageTarget == null || !damageTarget.IsAlive || (client && target.IsHost)) return;
            if (!estimateEventLogged)
            {
                estimateEventLogged = true;
                Instance.Logger.LogInfo("DPS estimate: received a target damage event.");
            }
            if (source == null)
            {
                if (!estimateMissingSourceLogged) Instance.Logger.LogInfo("DPS estimate: an event had no networked damage source.");
                estimateMissingSourceLogged = true;
                return;
            }
            Player player = AttackerResolver.Resolve(source);
            if (player == null)
            {
                if (!estimateUnknownSourceLogged) Instance.Logger.LogInfo("DPS estimate: could not match a damage source to a player (" + source.GetType().Name + ").");
                estimateUnknownSourceLogged = true;
                return;
            }
            if (player.IsLocalPlayer) return;
            Estimates.Add(player.OwnerClientId, Time.unscaledTime, data.damage);
            if (data.damage > 0 && !float.IsNaN(data.damage) && !float.IsInfinity(data.damage) && !estimateSuccessLogged)
            {
                estimateSuccessLogged = true;
                Instance.Logger.LogInfo("DPS estimate: recorded teammate damage.");
            }
        }
        catch (System.Exception e) { LogEstimateError(e); }
    }

    internal void LogEstimateDisplay()
    {
        if (estimateDisplayLogged) return;
        estimateDisplayLogged = true;
        Logger.LogInfo("DPS estimate: displayed a teammate estimate on the HUD.");
    }

    private static void LogEstimateError(System.Exception e)
    {
        if (!estimateErrorLogged && Instance != null)
        {
            estimateErrorLogged = true;
            Instance.Logger.LogWarning("DPS estimation missed a damage event: " + e);
        }
    }

    private static void OnTeammateInitialize(TeammateHUD __instance, Player player)
    {
        TeammateDpsLabel label = null;
        try
        {
            label = __instance.GetComponent<TeammateDpsLabel>();
            if (label == null) label = __instance.gameObject.AddComponent<TeammateDpsLabel>();
            if (!label.Initialize(__instance, player))
            {
                Destroy(label);
                if (Instance != null) Instance.Logger.LogWarning("DPS teammate label could not be created (TeammateHUD fields not found).");
            }
        }
        catch (System.Exception e)
        {
            if (label != null) Destroy(label);
            if (Instance != null) Instance.Logger.LogWarning("DPS teammate label could not be created: " + e);
        }
    }

    private void OnGUI()
    {
        Player player = Player.LocalPlayer;
        if (!ready || player == null || localLabel.Visible) return;
        GUI.Box(new Rect(20, 20, 420, 44), GUIContent.none);
        GUI.Label(new Rect(30, 28, 400, 24), fallbackText.Get(Window.Snapshot(Time.unscaledTime), false, false, true));
    }

    // Harmony patch classes. Awake applies them in groups, each group in its own try/catch.

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnLocalPlayerDamageTarget))]
    private static class LocalDamagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref DamageCallbackData data) => OnDamage(ref data);
    }

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnLocalPlayerKillTarget))]
    private static class LocalKillPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref KillCallbackData data) => OnLocalKill(ref data);
    }

    [HarmonyPatch(typeof(TeammateHUD), nameof(TeammateHUD.Initialize))]
    private static class TeammateInitializePatch
    {
        [HarmonyPostfix]
        private static void Postfix(TeammateHUD __instance, Player player) => OnTeammateInitialize(__instance, player);
    }

    [HarmonyPatch(typeof(Highlighter), nameof(Highlighter.OnDamageTarget))]
    private static class BurstDamagePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref DamageCallbackData data) => OnBurstDamage(ref data);
    }

    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.OnMissionStarted_Client))]
    private static class MissionStartedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MissionManager __instance) => OnMissionBoundary(__instance);
    }

    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.OnDestroy))]
    private static class MissionDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(MissionManager __instance) => OnMissionBoundary(__instance);
    }

    [HarmonyPatch(typeof(EnemyPart), nameof(EnemyPart.ApplyDamage_Server))]
    private static class EnemyDamageServerPatch
    {
        [HarmonyPrefix]
        private static void Prefix(EnemyPart __instance, DamageData data, IDamageSource source) => OnEnemyDamageServer(__instance, data, source);
    }

    [HarmonyPatch(typeof(ObjectTarget), nameof(ObjectTarget.ApplyDamage_Server))]
    private static class ObjectDamageServerPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ObjectTarget __instance, DamageData data, IDamageSource source) => OnObjectDamageServer(__instance, data, source);
    }

    [HarmonyPatch(typeof(EnemyPart), nameof(EnemyPart.Kill_Server))]
    private static class EnemyKillServerPatch
    {
        [HarmonyPrefix]
        private static void Prefix(EnemyPart __instance, IDamageSource source) => OnEnemyKillServer(__instance, source);
    }

    // A string: NetworkBehaviour.InitializeVariables is internal to Unity.Netcode.Runtime, which is not publicized.
    [HarmonyPatch(typeof(NetworkBehaviour), "InitializeVariables")]
    private static class NetworkVariablesPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NetworkBehaviour __instance) => OnNetworkVariablesInitialized(__instance);
    }
}
