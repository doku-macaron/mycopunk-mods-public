using BepInEx;
using HarmonyLib;

namespace MycopunkShared
{
    [BepInPlugin(MacaronCore.Guid, MacaronCore.Name, MacaronCore.Version)]
    // Client-side and not a sandbox mod: it adds a tab to the local options window and logs hotkey conflicts.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class MacaronCorePlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ModsMenu.Install(new Harmony(MacaronCore.Guid), this);
            Hotkeys.ReportConflicts(this);
            Logger.LogInfo(MacaronCore.Name + " " + MacaronCore.Version + " loaded.");
        }
    }
}
