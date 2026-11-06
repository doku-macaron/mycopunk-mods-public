using BepInEx;
using HarmonyLib;

namespace MycopunkShared
{
    [BepInPlugin(ModKit.Guid, ModKit.Name, ModKit.Version)]
    // Client-side and not a sandbox mod: it adds a tab to the local options window and logs hotkey conflicts.
    [MycoMod(null, ModFlags.IsClientSide)]
    public class ModKitPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ModsMenu.Install(new Harmony(ModKit.Guid), this);
            Hotkeys.ReportConflicts(this);
            Logger.LogInfo(ModKit.Name + " " + ModKit.Version + " loaded.");
        }
    }
}
