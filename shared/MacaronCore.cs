namespace MycopunkShared
{
    /// <summary>
    /// Macaron Core (was "Mycopunk Mod Kit" 1.0.0, renamed on request of the Mycopunk modding community: a library
    /// only doku_macaron's mods use should not look general). The shared code (MODS tab, hotkey report, teammate
    /// HUD lines, channels between mods, session log files) as one plugin those mods depend on, so they all use
    /// the same version of it. Each mod adds <c>[BepInDependency(MacaronCore.Guid, MacaronCore.Version)]</c> next to its BepInPlugin
    /// attribute.
    /// </summary>
    public static partial class MacaronCore
    {
        public const string Guid = "doku_macaron.mycopunk.macaroncore";
        public const string Name = "Macaron Core";
    }
}
