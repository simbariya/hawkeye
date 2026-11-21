namespace Hawkeye.Engine.Services.Odyle
{
    /// <summary>
    /// The game's Odyle refill: +15 to the current amount (not the pool), up to the 840 cap, on a GLOBAL timer
    /// shared by every character, every 3 hours.
    ///
    /// Derived 2026-11-21 from the owner's countdowns (cleric "1h 1 min" at 15:02, alta "44 min" at 15:14:45,
    /// Main "42 min" at 15:17, all pointing at about 16:00 UTC) and his "every 3 hrs"; then confirmed by AltB:
    /// 250 read at 15:13, 265 after the 16:00 refill. If the game ever drifts, change <see cref="AnchorUtc"/>.
    /// </summary>
    public static class OdyleRefill
    {
        public const int Amount = 15;
        public static readonly TimeSpan Interval = TimeSpan.FromHours(3);

        /// <summary>A known refill moment (UTC); every other one is a whole number of intervals from it.</summary>
        public static readonly DateTime AnchorUtc = new(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc);

        /// <summary>The first refill strictly after <paramref name="fromUtc"/>.</summary>
        public static DateTime NextAfter(DateTime fromUtc)
        {
            var ticks = (long)Math.Floor((fromUtc - AnchorUtc).Ticks / (double)Interval.Ticks) + 1;
            return AnchorUtc + TimeSpan.FromTicks(ticks * Interval.Ticks);
        }

        /// <summary>How many refills happened in (fromUtc, toUtc].</summary>
        public static int CountBetween(DateTime fromUtc, DateTime toUtc)
        {
            if (toUtc <= fromUtc) return 0;
            int n = 0;
            for (var t = NextAfter(fromUtc); t <= toUtc; t += Interval)
                if (++n > 10_000) break;   // years offline; the cap is reached long before
            return n;
        }

        /// <summary>
        /// Current Odyle now, from the last verified reading plus the refills since. Estimated = true when at least
        /// one refill was added (the game has not confirmed it yet).
        /// </summary>
        public static (int Current, bool Estimated) Estimate(int verifiedCurrent, DateTime verifiedAt, DateTime nowUtc)
        {
            int n = CountBetween(verifiedAt.ToUniversalTime(), nowUtc);
            if (n == 0 || verifiedCurrent >= OdyleParser.Cap) return (verifiedCurrent, false);
            return (Math.Min(OdyleParser.Cap, verifiedCurrent + n * Amount), true);
        }
    }
}
