namespace MycopunkShared
{
    /// <summary>
    /// Where a HUD element's anchor goes, from its two config values. Pure logic (floats only, no Unity types).
    ///
    /// The values are a point in the parent rect, 0..1 on each axis (what ModSettingsMenu's reposition mode writes:
    /// anchorMin clamped to 0..1). A negative value on either axis means "not moved yet": the element's default
    /// anchor is used, so the default can follow the game's own layout.
    /// </summary>
    public static class HudAnchor
    {
        public const float Unset = -1f;

        public static bool IsCustom(float x, float y) => x >= 0f && y >= 0f;

        public static void Resolve(float x, float y, float defaultX, float defaultY, out float anchorX, out float anchorY)
        {
            if (IsCustom(x, y))
            {
                anchorX = x > 1f ? 1f : x;
                anchorY = y > 1f ? 1f : y;
            }
            else
            {
                anchorX = defaultX;
                anchorY = defaultY;
            }
        }
    }
}
