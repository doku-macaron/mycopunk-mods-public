namespace MycopunkWeaponCycle
{
    /// <summary>
    /// The next gear slot for the scroll wheel and the weapon 1 key, with the stored (heavy) weapon in the cycle.
    /// Pure logic, no Unity.
    ///
    /// Slots (Player.SwapToSlot): 1 = weapon 1, 2 = weapon 2, 3 = utility, 4 = grenade, 6 = the stored gear (a
    /// heavy weapon picked up in the mission; Player.IsGearStored). The game's wheel goes (slot + 1) % 3 and its
    /// weapon 1 key goes 1 → 2, else 1, so the stored slot is never reached that way.
    /// </summary>
    internal static class WeaponCycle
    {
        public const int Weapon1 = 1;
        public const int Weapon2 = 2;
        public const int Stored = 6;

        /// <summary>Wheel: forward 1 → 2 → stored → 1, backward the other way. From any other slot, forward goes
        /// to the first and backward to the last, like the game.</summary>
        public static int Wheel(int current, bool forward, bool hasStored)
        {
            int last = hasStored ? Stored : Weapon2;
            switch (current)
            {
                case Weapon1: return forward ? Weapon2 : last;
                case Weapon2: return forward ? (hasStored ? Stored : Weapon1) : Weapon1;
                case Stored when hasStored: return forward ? Weapon1 : Weapon2;
                default: return forward ? Weapon1 : last;
            }
        }

        /// <summary>
        /// Wheel, skipping the slots <paramref name="skip"/> says (a gun with both magazine and reserve empty): it
        /// keeps stepping the same way from <see cref="Wheel(int, bool, bool)"/>'s answer, never onto the current slot.
        /// When every other weapon would be skipped, it stays on the current one if that is a weapon (returns
        /// <paramref name="current"/>: no switch; 0.2.0 switched as usual, which with two weapons meant the empty one
        /// was never skipped), else (from the utility or grenade slot) it gives the plain answer.
        /// </summary>
        public static int Wheel(int current, bool forward, bool hasStored, System.Func<int, bool> skip)
        {
            int first = Wheel(current, forward, hasStored);
            int slot = first;
            for (int i = 0; i < 3; i++)
            {
                if (slot != current && !skip(slot))
                    return slot;
                slot = Wheel(slot, forward, hasStored);
            }
            bool onWeapon = current == Weapon1 || current == Weapon2 || (current == Stored && hasStored);
            return onWeapon ? current : first;
        }

        /// <summary>Weapon 1 key (plan A, chosen by the user): each press goes 1 → 2 → stored → 1; from any other
        /// slot it takes weapon 1.</summary>
        public static int Weapon1Key(int current, bool hasStored)
        {
            switch (current)
            {
                case Weapon1: return Weapon2;
                case Weapon2: return hasStored ? Stored : Weapon1;
                default: return Weapon1;
            }
        }
    }
}
