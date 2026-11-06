namespace MycopunkAbilityCooldown
{
    /// <summary>What the ability box shows. Pure logic, no Unity.</summary>
    internal enum CooldownMode
    {
        /// <summary>Nothing over the icon: full, or not recharging over time.</summary>
        None,
        /// <summary>No charge left (cannot be used): dark shade and big seconds in the middle.</summary>
        Empty,
        /// <summary>At least one charge left but not full (can be used): small seconds in the bottom-right corner.</summary>
        Recharging,
    }

    /// <summary>
    /// The display for abilities with stacks (MaxCharges > 1, the photon disc for one), like the stack counters of
    /// OW or LoL: while a charge is left the ability is usable, so it is not shaded, only a small timer for the next
    /// charge; the number of charges sits in the top-right corner. Abilities with one charge look as before.
    /// </summary>
    internal readonly struct CooldownView
    {
        public readonly CooldownMode Mode;
        /// <summary>Whole charges ready now.</summary>
        public readonly int Count;
        /// <summary>Show <see cref="Count"/> in the top-right corner (only for abilities with stacks).</summary>
        public readonly bool ShowCount;

        private CooldownView(CooldownMode mode, int count, bool showCount)
        {
            Mode = mode;
            Count = count;
            ShowCount = showCount;
        }

        /// <param name="seconds">From <see cref="RechargeEstimate"/>; NaN when not recharging over time.</param>
        public static CooldownView Of(float charge, int maxCharges, float seconds)
        {
            // Plain floor, like the game: it lets an ability be used only from Charge >= 1 (Player.SwapToSlot).
            int count = charge <= 0f ? 0 : (int)System.Math.Floor(charge);
            if (count > maxCharges)
                count = maxCharges;
            bool showCount = maxCharges > 1;
            if (float.IsNaN(seconds) || count >= maxCharges)
                return new CooldownView(CooldownMode.None, count, showCount);
            return new CooldownView(count == 0 ? CooldownMode.Empty : CooldownMode.Recharging, count, showCount);
        }
    }
}
