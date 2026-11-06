using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using TMPro;
using UnityEngine;

namespace MycopunkHealthNumbers
{
    /// <summary>
    /// Shows a teammate's "HP health / max" on a line under their name in the game's teammate HUD.
    ///
    /// Every client already has each teammate's health (Player.syncedHealth is copied into Player.health)
    /// and max health (set by OnUpgradesApplied_Rpc and GetDataOnLoad_ClientRpc); the game's own ring is
    /// drawn from the same two values in TeammateHUD.OnHealthChanged. So nothing has to be sent over the
    /// network and only the local player needs this mod. The temporary max-health cap (Mycologist) is
    /// computed per frame on the owner's machine only, so it is not shown for teammates.
    ///
    /// The line comes from <see cref="TeammateHudLines"/> (order 50, above DPS Checker's line at 100), which makes
    /// room for it. Up to 0.3.1 the text sat at the right end of TeammateHUD.healthbar, which turned out to be the
    /// ring around the portrait: that spot is the line under the name, where DPS Checker's line is.
    /// While the teammate is dead (HideWhenDead) the line stays and is left empty, so the lines below do not jump;
    /// with General.Enabled or Teammates.Enabled off the line is removed.
    /// </summary>
    internal sealed class TeammateHealthLabel : MonoBehaviour
    {
        internal const string LineId = "t4t5u0.healthnumbers";
        internal const int LineOrder = 50;

        private static readonly System.Reflection.FieldInfo NameField = AccessTools.Field(typeof(TeammateHUD), "nameText");
        private static readonly System.Reflection.FieldInfo PlayerField = AccessTools.Field(typeof(TeammateHUD), "player");

        private TeammateHUD _hud;
        private Player _player;
        private TextMeshProUGUI _nameText;
        private TextMeshProUGUI _label;
        private readonly HealthText _text = new HealthText();
        private string _shown;

        public static void Install(Harmony harmony)
        {
            var init = AccessTools.Method(typeof(TeammateHUD), "Initialize");
            if (init == null || !UnityTeammateHud.Available || NameField == null)
            {
                Plugin.Log.LogWarning("TeammateHUD.Initialize or its fields not found; teammate health numbers are off.");
                return;
            }
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(TeammateHealthLabel), nameof(Initialize_Postfix)));
            if (PlayerField != null)
                foreach (var hud in Object.FindObjectsByType<TeammateHUD>(FindObjectsInactive.Include))
                    if (PlayerField.GetValue(hud) is Player player) Initialize_Postfix(hud, player);
        }

        private static void Initialize_Postfix(TeammateHUD __instance, Player player)
        {
            try
            {
                var label = __instance.GetComponent<TeammateHealthLabel>();
                if (label == null)
                    label = __instance.gameObject.AddComponent<TeammateHealthLabel>();
                label._hud = __instance;
                label._player = player;
                label._nameText = NameField.GetValue(__instance) as TextMeshProUGUI;
                label._label = null; // re-acquired (the line may have been laid out again)
                label._shown = null;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Teammate health label could not be created: " + e);
            }
        }

        private void LateUpdate()
        {
            if (_hud == null || _nameText == null)
                return;
            try
            {
                var s = Plugin.Settings;
                bool wanted = s != null && s.Enabled && s.Teammates && _player != null;
                if (!wanted)
                {
                    if (_label != null)
                    {
                        UnityTeammateHud.Remove(_hud, LineId);
                        _label = null;
                        _shown = null;
                    }
                    return;
                }
                if (_label == null)
                {
                    _label = UnityTeammateHud.AddText(_hud, LineId, LineOrder);
                    if (_label == null)
                    {
                        Plugin.Log.LogWarning("Teammate health line could not be added; teammate health numbers are off for this HUD.");
                        enabled = false;
                        return;
                    }
                }

                // HealthText returns the same string instance while the values do not change, so the "HP " text is
                // only rebuilt when they do.
                string inner = s.HideWhenDead && !_player.IsAlive
                    ? ""
                    : _text.Get(_player.Health, _player.MaxHealth, 1f, s.Decimals, s.Format, "");
                if (!ReferenceEquals(inner, _shown))
                {
                    _shown = inner;
                    string text = inner.Length == 0 ? "" : "HP " + inner;
                    if (_label.text != text)
                        _label.text = text;
                }

                float size = _nameText.fontSize * s.TeammateFontScale;
                if (_label.fontSize != size)
                    _label.fontSize = size;
                Color color = s.TeammateNameColor ? _nameText.color : s.Color;
                if (_label.color != color)
                    _label.color = color;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Teammate health label failed and is turned off: " + e);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            // Only this component going away (plugin unloaded): close the gap. With the whole HUD it is harmless.
            if (_hud != null && _label != null)
                UnityTeammateHud.Remove(_hud, LineId);
        }
    }
}
