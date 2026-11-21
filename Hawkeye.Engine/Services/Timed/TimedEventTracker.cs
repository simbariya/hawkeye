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
        private readonly Lock gate = new();
        private readonly Dictionary<uint, (DateTime StartUtc, TimeSpan Duration)> timers = new();

        public void Register(TimedEvent evt)
        {
            lock (gate) timers[evt.Id] = (DateTime.UtcNow, evt.Duration);
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
