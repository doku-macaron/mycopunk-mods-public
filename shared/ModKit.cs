namespace MycopunkShared
{
    /// <summary>
    /// Mycopunk Mod Kit: the shared code (MODS tab, hotkey report, teammate HUD lines, channels between mods,
    /// session log files) as one plugin every mod depends on, so all mods use the same version of it.
    /// Each mod adds <c>[BepInDependency(ModKit.Guid)]</c> next to its BepInPlugin attribute.
    /// </summary>
    public static class ModKit
    {
        public const string Guid = "doku_macaron.mycopunk.modkit";
        public const string Name = "Mycopunk Mod Kit";
        public const string Version = "1.0.0";
    }
}
