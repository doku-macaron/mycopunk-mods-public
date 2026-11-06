namespace MycopunkXpNumbers
{
    /// <summary>
    /// "1,200 / 3,000 XP" for a level's progress. Pure logic, no Unity.
    ///
    /// The game's bar is GearData.levelXP / NextLevelXPCost (XP into the current level / XP the next level costs),
    /// and GetLevelString shows "∞" from MaxLevel on, where there is no next level: then this is empty.
    /// </summary>
    internal static class XpText
    {
        public static string Progress(int levelXP, int nextLevelCost, int level, int maxLevel)
        {
            if (level >= maxLevel || nextLevelCost <= 0)
                return "";
            return levelXP.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " / " +
                   nextLevelCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " XP";
        }

        /// <summary>The game's EaseFunctions.EaseOutQuartic: 1 - (1 - t)^4.</summary>
        public static float EaseOutQuartic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u * u;
        }

        /// <summary>
        /// The level, the XP into it and its cost at a fractional level v (the mission results animate the bar
        /// through v = level + levelXP / cost(level + 1)): level = floor(v), XP = round((v - level) * cost).
        /// </summary>
        public static void AtFractionalLevel(float v, System.Func<int, int> costOf, out int level, out int xp, out int cost)
        {
            level = (int)System.Math.Floor(v);
            cost = costOf(level + 1);
            xp = (int)System.Math.Round((v - level) * cost);
            if (xp >= cost && cost > 0)
                xp = cost - 1; // rounding up to a full bar would read as the next level
            if (xp < 0)
                xp = 0;
        }
    }
}
