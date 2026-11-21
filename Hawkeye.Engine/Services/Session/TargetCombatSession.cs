using Hawkeye.Core.Models;
using Hawkeye.Engine.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Settings;
using System.Data;
using static System.Net.Mime.MediaTypeNames;

namespace Hawkeye.Engine.Services.Session
{
    public enum SessionState
    {
        Active,
        Completed
    }

    public sealed class TargetCombatSession
    {
        private readonly Dictionary<long, PlayerSession> playerSessions = new();
        private readonly EntityTracker entityTracker;

        public Guid SessionId { get; } = Guid.NewGuid();
        public int TargetId { get; }
        public Mob TargetInfo { get; }
        public DateTime SessionStart { get; }
        public DateTime LastHitTime { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public SessionState State { get; private set; } = SessionState.Active;
        public bool IsCompleted => State == SessionState.Completed;

        private readonly BuffEventManager buffEventManager = new();

        private long LastKnownTargetHp { get; set; } = -1;
        private readonly ISettingsService settingsService;


        public TargetCombatSession(Mob targetInfo, DateTime sessionStart, EntityTracker entityTracker, ISettingsService settingsService)
        {
            TargetId = targetInfo.Id;
            TargetInfo = targetInfo;
            this.settingsService = settingsService;
            SessionStart = sessionStart;
            LastHitTime = sessionStart;
            LastAttemptTime = sessionStart;
            this.entityTracker = entityTracker;

        }

        /// <summary>Last time anyone hit this target, counted or not (a finished dummy run still sees hits).</summary>
        public DateTime LastAttemptTime { get; private set; }

        /// <summary>Frozen fights keep their numbers on screen and take no more hits (a stopped dummy run).</summary>
        public bool IsFrozen { get; private set; }
        public void Freeze() => IsFrozen = true;

        /// <summary>Length of a Timed dummy run, or null when this fight has no time limit.</summary>
        private TimeSpan? RunLimit =>
            TargetInfo.IsDummy && settingsService.Current.DummyMode == DummyMode.Timed
                ? TimeSpan.FromMinutes(Math.Clamp(settingsService.Current.DummyMinutes, 1, 3))
                : null;

        public void AddDamage(PlayerDamage damage)
        {
            if (damage.DateTime > LastAttemptTime) LastAttemptTime = damage.DateTime;
            if (IsFrozen) return;
            if (RunLimit is { } limit && damage.DateTime - SessionStart > limit) return; // the timed run is over

            if (!playerSessions.TryGetValue(damage.SourceEntity.Id, out var session))
            {
                session = new PlayerSession(damage.SourceEntity.Id, entityTracker);
                playerSessions[damage.SourceEntity.Id] = session;
            }

            session.AddDamage(damage);

            if (damage.DateTime > LastHitTime)
                LastHitTime = damage.DateTime;
            LastKnownTargetHp = TargetInfo.HpCurrent;
        }

        public void RegisterPlayerDeath(int playerId)
        {
            if (TargetInfo.HpCurrent == 0) return;
            if (!playerSessions.TryGetValue(playerId, out var session)) return;
            session.RegisterPlayerDeath();
        }



        public void ProcessBuffEvent(BuffEvent buffEvent)
        {
            if (IsCompleted) return;
            var buffEnd = buffEvent.AppliedAt.AddMilliseconds(buffEvent.DurationMs);
            if (buffEnd <= SessionStart) return;
            buffEventManager.Add(buffEvent);
        }


        public bool IsNewTry()
        {
            
            if (TargetInfo.IsDummy) return false;
            if (LastKnownTargetHp == -1) return false;
            if (TargetInfo.HpCurrent > LastKnownTargetHp && (DateTime.Now - LastHitTime) > TimeSpan.FromSeconds(5)) return true;
            return false;
        }


        public IReadOnlyCollection<int> GetPlayerEntityIds()
        {
            return playerSessions.Keys.Select(k => (int)k).ToList();
        }

        public void Complete()
        {
            if (IsCompleted) return;
            State = SessionState.Completed;
            CompletedAt = LastHitTime;
        }

        public TimeSpan GetCombatDuration()
        {
            var duration = LastHitTime - SessionStart;
            if (RunLimit is { } limit && duration > limit) duration = limit;
            return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        }

        public IReadOnlyCollection<PlayerStats> GetPlayerStats()
        {
            var activeSessions = playerSessions.Values
                .Where(s => s.HitCount > 0)
                .ToList();

            if (activeSessions.Count == 0)
                return [];

            long totalDamage = activeSessions.Sum(s => s.TotalDamage);

            return activeSessions
                .Select(s => DamageStatisticsCalculator.ComputePlayerStats(s, totalDamage))
                .OrderByDescending(s => s.TotalDamage)
                .ToList();
        }

        public IReadOnlyList<PlayerDamage> GetCombatLog(long playerId)
        {
            if (!playerSessions.TryGetValue(playerId, out var session))
                return [];

            return [.. session.Hits.AsEnumerable().Reverse()];
        }

        public IReadOnlyCollection<SkillStats> GetSkillStats(long playerId)
        {
            if (!playerSessions.TryGetValue(playerId, out var session))
                return [];

            return DamageStatisticsCalculator.ComputeSkillStats(session, settingsService.Current.GroupSummonDamage);
        }

        public IReadOnlyList<BuffEvent> GetBuffEvents(long playerId, DateTime from, DateTime to)
            => buffEventManager.GetBuffEvents((int)playerId, from, to);

        public int CountRecentHits(DateTime cutoff)
            => playerSessions.Values.Sum(s => s.CountHitsAfter(cutoff));

        public DateTime? GetUserLastHitTime()
            => playerSessions.Values
                .Where(s => s.IsUser && s.LastHit.HasValue)
                .Select(s => s.LastHit!.Value)
                .Cast<DateTime?>()
                .Max();

        public void Reset()
        {
            foreach (var session in playerSessions.Values)
                session.Reset();
            playerSessions.Clear();
            buffEventManager.Reset();
        }

        /// <summary>
        /// Transfers all damage recorded under <paramref name="summonId"/> to the owner's player session.
        /// Returns:
        ///   -1  – no summon session found in this combat session (nothing to do)
        ///    0  – summon session found but owner player entity is unknown; session kept as-is,
        ///         a placeholder entity named "Summon_{summonId}" is registered in the tracker
        ///   >0  – number of hits successfully transferred to the owner's session
        /// </summary>
        public int TransferSummonDamage(int summonId, int ownerId)
        {
            if (!playerSessions.TryGetValue(summonId, out var summonSession))
                return -1;

            var ownerEntity = entityTracker.GetPlayerEntity(ownerId);
            if (ownerEntity is null)
                return 0;

            if (!playerSessions.TryGetValue(ownerId, out var ownerSession))
            {
                ownerSession = new PlayerSession(ownerId, entityTracker);
                playerSessions[ownerId] = ownerSession;
            }

            foreach (var hit in summonSession.Hits)
            {
                var reStamped = new PlayerDamage
                {
                    DateTime       = hit.DateTime,
                    SourceEntity   = ownerEntity,
                    SourceSummon   = hit.SourceEntity,
                    TargetEntity   = hit.TargetEntity,
                    Skill          = hit.Skill,
                    CharacterClass = hit.CharacterClass,
                    Damage         = hit.Damage,
                    IsCritical     = hit.IsCritical,
                    IsBackAttack   = hit.IsBackAttack,
                    IsFrontAttack  = hit.IsFrontAttack,
                    IsPerfect      = hit.IsPerfect,
                    IsDoubleDamage = hit.IsDoubleDamage,
                    IsParry        = hit.IsParry,
                    IsDot          = hit.IsDot,
                    MultihitCunt   = hit.MultihitCunt,
                    MultihitDamage = hit.MultihitDamage,
                };
                ownerSession.AddDamage(reStamped);
            }

            var transferred = summonSession.Hits.Count;
            playerSessions.Remove(summonId);
            return transferred;
        }
    }
}