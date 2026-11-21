namespace Hawkeye.Engine.Services.Timed
{
    public sealed class TimedEvent
    {
        public required uint Id { get; init; }
        public required string Name { get; init; }
        public required string IconUrl { get; init; }
        public required TimeSpan Duration { get; init; }
    }

    public sealed class TimedItemState
    {
        public required uint Id { get; init; }
        public required TimeSpan Duration { get; init; }
        public required TimeSpan TimeLeft { get; init; }
    }

    public interface ITimedEventTracker
    {
        /// <summary>Start (or restart) the timer for <see cref="TimedEvent.Id"/>.</summary>
        void Register(TimedEvent evt);

        /// <summary>
        /// The game says this much time is LEFT. A running timer keeps its full length and only moves how much
        /// is left (a cooldown cut by another skill makes the sweep jump forward, it does not start over).
        /// Longer than the current length, or nothing running: starts a new timer. Zero: clears it.
        /// </summary>
        void UpdateTimeLeft(TimedEvent evt);

        /// <summary>The running timer for a skill id, or null if it isn't running.</summary>
        TimedItemState? Get(uint id);

        void Clear();
    }

    /// <summary>
    /// A set of countdown timers keyed by skill id. The app keeps two: one for your cooldowns, one for buffs
    /// you have up (or have put on something). Thread-safe: the packet thread writes, the UI thread reads.
    /// </summary>
    public sealed class TimedEventTracker : ITimedEventTracker
    {
        /// <summary>How soon after a timer starts an update still counts as "the real full length".</summary>
        private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(1500);

        private readonly Lock gate = new();
        private readonly Dictionary<uint, (DateTime StartUtc, TimeSpan Duration)> timers = new();

        public void Register(TimedEvent evt)
        {
            lock (gate) timers[evt.Id] = (DateTime.UtcNow, evt.Duration);
        }

        public void UpdateTimeLeft(TimedEvent evt)
        {
            var now = DateTime.UtcNow;
            lock (gate)
            {
                if (evt.Duration <= TimeSpan.Zero)
                {
                    timers.Remove(evt.Id);
                    return;
                }

                if (timers.TryGetValue(evt.Id, out var t)
                    && t.Duration - (now - t.StartUtc) > TimeSpan.Zero   // still running
                    && evt.Duration <= t.Duration)
                {
                    var elapsed = now - t.StartUtc;
                    if (elapsed < SettleWindow)
                    {
                        // First word from the game right after the cast: the cast only knew the BASE cooldown,
                        // this is the real one (gear and buffs shorten it). Use it as the full length.
                        timers[evt.Id] = (t.StartUtc, elapsed + evt.Duration);
                        return;
                    }
                    // Keep the full length; re-anchor the start so that exactly evt.Duration is left.
                    timers[evt.Id] = (now - (t.Duration - evt.Duration), t.Duration);
                    return;
                }

                timers[evt.Id] = (now, evt.Duration);
            }
        }

        public TimedItemState? Get(uint id)
        {
            lock (gate)
            {
                if (!timers.TryGetValue(id, out var t)) return null;

                var left = t.Duration - (DateTime.UtcNow - t.StartUtc);
                if (left <= TimeSpan.Zero)
                {
                    timers.Remove(id);
                    return null;
                }
                return new TimedItemState { Id = id, Duration = t.Duration, TimeLeft = left };
            }
        }

        public void Clear()
        {
            lock (gate) timers.Clear();
        }
    }
}
