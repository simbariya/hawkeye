using Hawkeye.Core.Models;
using Hawkeye.Engine.Services.Entity;

namespace Hawkeye.Engine.Services.Session
{
    public sealed class ActiveTargetResolver
    {
        private static readonly TimeSpan RecentHitWindow = TimeSpan.FromSeconds(5);

        private readonly EntityTracker entityTracker;

        public int? ActiveTargetId { get; private set; }

        public ActiveTargetResolver(EntityTracker entityTracker)
        {
            this.entityTracker = entityTracker;
        }

        public void Update(IEnumerable<TargetEntry> entries, DateTime lastHitTime)
        {
            var cutoff = lastHitTime - RecentHitWindow;

            var activeEntries = entries
                .Where(e => e.CurrentSession is not null && !e.CurrentSession.IsCompleted)
                .ToList();

            // Stick to YOUR fight: the target you hit most recently, however long ago, as long as that
            // fight is still open. Someone else's fight nearby never takes the meter from you
            // (owner, 2026-11-21: "in-game meter sticks to my damage"). Only when you have hit
            // nothing open does it fall back to the busiest fight.
            var userBest = activeEntries
                .Select(e => (entry: e, userLastHit: e.GetUserLastHitTime()))
                .Where(x => x.userLastHit.HasValue)
                .OrderByDescending(x => x.userLastHit!.Value)
                .FirstOrDefault();

            if (userBest.entry is not null)
            {
                ActiveTargetId = userBest.entry.TargetId;
                return;
            }

            // Once Hawkeye knows who you are, someone else's fight never takes the meter (owner, 2026-11-21:
            // "not overwrite until I am involved"). The meter then shows your last fight from history.
            if (entityTracker.HasCurrentUser)
            {
                ActiveTargetId = null;
                return;
            }

            var best = activeEntries
                .Select(e => (entry: e, count: e.CountRecentHits(cutoff)))
                .Where(x => x.count > 0)
                .OrderByDescending(x => x.count)
                .FirstOrDefault();

            ActiveTargetId = best.entry?.TargetId;
        }

        public Mob? GetActiveTargetMob()
        {
            if (ActiveTargetId is not { } targetId) return null;
            return entityTracker.GetTargetMob(targetId);
        }

        public void Reset() => ActiveTargetId = null;
    }
}