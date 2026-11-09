using BepInEx;
using HarmonyLib;

namespace MycopunkShared
{
    [BepInPlugin(MacaronCore.Guid, MacaronCore.Name, MacaronCore.Version)]
    // Client-side and not a sandbox mod: it adds a tab to the local options window and logs hotkey conflicts.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class MacaronCorePlugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(MacaronCore.Guid);
            ModsMenu.Install(_harmony, this);
            Hotkeys.ReportConflicts(this);
            Logger.LogInfo(MacaronCore.Name + " " + MacaronCore.Version + " loaded.");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
