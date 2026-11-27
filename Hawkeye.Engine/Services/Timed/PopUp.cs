namespace Hawkeye.Engine.Services.Timed
{
    /// <summary>
    /// Tracker icons that pop up near the end (v0.2.38; owner, 2026-11-26: "a pop up 3 sec before you need to use it
    /// for skills"; then "so can configure that it only appears when x seconds left until buff expires or cd almost
    /// ready"). With "pop up at N s" set (TrackedSkill.PopUpSeconds), an icon is on screen from N seconds before its
    /// cooldown ends or its buff runs out until <see cref="Linger"/> after. 0 = always, as before.
    /// </summary>
    public static class PopUp
    {
        /// <summary>How long a popped-up icon stays once the cooldown is ready or the buff ran out.</summary>
        public static readonly TimeSpan Linger = TimeSpan.FromSeconds(3);

        /// <param name="popUpSeconds">0 = always on screen.</param>
        /// <param name="timeLeft">Time left on the icon's timer; null when none is running.</param>
        /// <param name="lastActiveUtc">The last moment a timer was running on the icon.</param>
        public static bool Visible(int popUpSeconds, TimeSpan? timeLeft, DateTime lastActiveUtc, DateTime nowUtc)
        {
            if (popUpSeconds <= 0) return true;
            if (timeLeft is { } left && left > TimeSpan.FromMilliseconds(50)) return left <= TimeSpan.FromSeconds(popUpSeconds);
            return nowUtc - lastActiveUtc <= Linger;
        }

        /// <summary>
        /// Whether a tracker icon is on screen. While Settings is open: always (to place it). An icon set to pop up
        /// follows only its own timing, anywhere: in PvP, the arena, out of combat (v0.2.42; owner, 2026-11-27: "the
        /// skill icons do not appear". Until then a pop-up icon also needed a boss or dummy fight, or "Always show",
        /// so in PvP it never came). Any other icon: during a boss or dummy fight, or always with "Always show".
        /// </summary>
        public static bool OnScreen(bool settingsOpen, bool fightOrAlways, int popUpSeconds, TimeSpan? timeLeft,
            DateTime lastActiveUtc, DateTime nowUtc) =>
            settingsOpen || (popUpSeconds > 0 ? Visible(popUpSeconds, timeLeft, lastActiveUtc, nowUtc) : fightOrAlways);
    }
}
