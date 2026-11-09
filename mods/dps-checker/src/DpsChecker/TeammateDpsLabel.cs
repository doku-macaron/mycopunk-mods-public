using MycopunkShared;
using Pigeon.Movement;
using TMPro;
using UnityEngine;

/// <summary>
/// The DPS line under a teammate's name. The text comes from <see cref="TeammateDpsText"/>; the line and the room
/// for it from <see cref="TeammateHudLines"/>, which other mods use for their lines too.
/// </summary>
public sealed class TeammateDpsLabel : MonoBehaviour
{
    internal const string LineId = "f1w3.dpschecker";
    internal const int LineOrder = 100;

    private static readonly TeammateDpsText.StatsSource FromPlugin = delegate(ulong id, out CombatStats stats, out bool dpsEstimated, out bool totalsEstimated)
    {
        stats = default(CombatStats);
        dpsEstimated = totalsEstimated = true;
        return Plugin.Instance != null && Plugin.Instance.TryGetRemoteStats(id, out stats, out dpsEstimated, out totalsEstimated);
    };

    private Player player;
    private TeammateHUD owner;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI label;
    private float nextUpdate;
    private readonly TeammateDpsText text = new TeammateDpsText(FromPlugin, () => { if (Plugin.Instance != null) Plugin.Instance.LogEstimateDisplay(); });

    /// <summary>From the TeammateHUD.Initialize postfix. False when the line could not be made.</summary>
    internal bool Initialize(TeammateHUD hud, Player teammate)
    {
        owner = hud;
        player = teammate;
        nameText = hud.nameText;
        label = UnityTeammateHud.AddText(hud, LineId, LineOrder);
        if (label == null || nameText == null) return false;
        nextUpdate = 0;
        Refresh();
        return true;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.25f;
        Refresh();
    }

    private void Refresh()
    {
        if (label == null || nameText == null || player == null) return;
        string value = text.Get(player.OwnerClientId);
        MycopunkShared.GameHudText.SetText(label, value);
        label.color = nameText.color;
        label.fontSize = nameText.fontSize;
        UnityTeammateHud.Place(owner);
    }

    private void OnDestroy()
    {
        // Closes the gap when only this component goes (plugin unloaded). When the whole HUD is destroyed this
        // just tidies a HUD that is going away anyway.
        if (owner != null) UnityTeammateHud.Remove(owner, LineId);
    }
}
