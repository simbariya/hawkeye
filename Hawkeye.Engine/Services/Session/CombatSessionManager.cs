using Hawkeye.Core.Models;
using Hawkeye.Engine.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Hawkeye.Engine.Services.Timed;
using Microsoft.Extensions.DependencyInjection;

namespace Hawkeye.Engine.Services.Session
{
    public sealed class CombatSessionManager
    {
        public event EventHandler<int>? PingUpdated;

        private readonly ConcurrentDictionary<int, TargetEntry> targetEntries = new();
        private readonly ActiveTargetResolver targetResolver;
        private readonly EntityTracker entityTracker;
        private readonly ILogger<CombatSessionManager> logger;
        private readonly Lock lockObject = new();
        private readonly ISettingsService settingsService;
        private PlayerStatSnapshot? latestPlayerStatSnapshot;
        private readonly List<BuffEvent> activeBuffBacklog = new();
        private readonly ITimedEventTracker buffEventTracker;
        private readonly ITimedEventTracker skillCdEventTracker;


        public CombatSessionManager(
            EntityTracker entityTracker,
            ILoggerFactory loggerFactory,
            ISettingsService settingsService,
            [FromKeyedServices(TimerKeys.Buffs)] ITimedEventTracker buffEventTracker,
            [FromKeyedServices(TimerKeys.Cooldowns)] ITimedEventTracker skillCdEventTracker)
        {
            this.entityTracker = entityTracker;
            this.settingsService = settingsService;
            this.buffEventTracker = buffEventTracker;
            this.skillCdEventTracker = skillCdEventTracker;
            targetResolver = new ActiveTargetResolver(entityTracker);
            logger = loggerFactory.CreateLogger<CombatSessionManager>();
            entityTracker.SummonRegistered += OnSummonRegistered;
            RefreshTrackedIds();
            lastDummyMode = settingsService.Current.DummyMode;
            settingsService.Changed += (_, _) => OnSettingsChanged();
        }


        public void FirePingUpdate(int pingMs)
        {
            PingUpdated?.Invoke(this, pingMs);
        }
      
        public TargetCombatSession? GetActiveTargetSession()
        {
            lock (lockObject)
            {
                if (targetResolver.ActiveTargetId is not { } id) return null;
                return targetEntries.TryGetValue(id, out var entry) ? entry.CurrentSession : null;
            }
        }

      
        public IEnumerable<TargetCombatSession> GetTargetHistory(int targetId)
        {
            lock (lockObject)
            {
                return targetEntries.TryGetValue(targetId, out var entry)
                    ? entry.AllSessions.ToList()
                    : [];
            }
        }
      
        public IReadOnlyCollection<PlayerStats> PlayerStats
        {
            get { lock (lockObject) { return GetActiveTargetSession()?.GetPlayerStats() ?? []; } }
        }

        public PlayerStatSnapshot? GetCurrentPlayerStatSnapshot()
        {
            lock (lockObject)
            {
                return latestPlayerStatSnapshot;
            }
        }

        public void ProcessPlayerStatsUpdate(PlayerCharacterStats stats, DateTime? capturedAt = null)
        {
            ArgumentNullException.ThrowIfNull(stats);

            lock (lockObject)
            {
                latestPlayerStatSnapshot = PlayerStatSnapshot.Create(stats, capturedAt);
            }
        }

        // ---- Deaths on a boss, across pulls ---------------------------------------------------------
        // Kept per boss TYPE (mob code), not per entity, so a wipe and re-pull (even if the boss
        // respawns as a new entity) keeps adding up. Starts again after the boss is killed.
        private sealed class BossDeaths
        {
            public readonly HashSet<int> Fighters = new();
            public readonly Dictionary<int, int> Deaths = new();
            public bool SawHp;
            public bool Killed;
        }

        private readonly Dictionary<int, BossDeaths> deathsByBoss = new();

        private void NoteBossFighter(Mob mob, int playerId)
        {
            if (!deathsByBoss.TryGetValue(mob.MobCode, out var tally))
                deathsByBoss[mob.MobCode] = tally = new BossDeaths();

            if (tally.Killed && mob.HpCurrent > 0)
            {
                // A fresh fight after a kill: start counting again.
                deathsByBoss[mob.MobCode] = tally = new BossDeaths();
            }

            if (mob.HpCurrent > 0) tally.SawHp = true;
            else if (tally.SawHp) tally.Killed = true;

            tally.Fighters.Add(playerId);
        }

        public void RegisterPlayerDeath(int playerId)
        {
            lock (lockObject)
            {
                var session = GetActiveTargetSession();
                if (session is null) return;
                session.RegisterPlayerDeath(playerId);

                var mob = session.TargetInfo;
                if (mob.IsDummy || !deathsByBoss.TryGetValue(mob.MobCode, out var tally)) return;
                if (tally.Killed || !tally.Fighters.Contains(playerId)) return; // not in this fight, or after the kill
                tally.Deaths[playerId] = tally.Deaths.GetValueOrDefault(playerId) + 1;
                logger.LogInformation("[DEATH] player {PlayerId} died on {Boss} ({Count} so far)", playerId, mob.Name, tally.Deaths[playerId]);
            }
        }

        /// <summary>Deaths per player on the boss the meter is showing (all pulls since its last kill).</summary>
        public IReadOnlyDictionary<int, int> GetActiveBossDeaths()
        {
            lock (lockObject)
            {
                var mob = GetActiveTargetSession()?.TargetInfo;
                if (mob is null || mob.IsDummy || !deathsByBoss.TryGetValue(mob.MobCode, out var tally)) return new Dictionary<int, int>();
                return new Dictionary<int, int>(tally.Deaths);
            }
        }

        /// <summary>How long after the last hit a boss fight still counts as "on" for the skill tracker.</summary>
        public static readonly TimeSpan BossFightGrace = TimeSpan.FromSeconds(15);

        /// <summary>
        /// True while you are in a boss (or training dummy) fight: the shown fight is open, not a stopped
        /// dummy run, and someone hit it within BossFightGrace. Drives "tracker only in boss fights".
        /// </summary>
        public bool IsInBossFight(DateTime now)
        {
            lock (lockObject)
            {
                var session = GetActiveTargetSession();
                return session is not null && !session.IsCompleted && !session.IsFrozen
                       && now - session.LastAttemptTime <= BossFightGrace;
            }
        }

        // ---- Training dummy, Start/Stop mode ----------------------------------------------------------
        public enum DummyRunState { Idle, Armed, Running, Stopped }

        public DummyRunState DummyRun { get; private set; } = DummyRunState.Idle;
        private int? dummyRunTargetId;
        private DummyMode lastDummyMode;

        /// <summary>Clears any dummy numbers and waits for your next hit on a dummy.</summary>
        public void StartDummyRun()
        {
            lock (lockObject)
            {
                foreach (var (id, entry) in targetEntries.ToList())
                {
                    if (entry.CurrentSession?.TargetInfo.IsDummy != true) continue;
                    entry.Reset();
                    targetEntries.TryRemove(id, out _);
                }
                if (targetResolver.ActiveTargetId is { } active && !targetEntries.ContainsKey(active)) targetResolver.Reset();
                dummyRunTargetId = null;
                DummyRun = DummyRunState.Armed;
                logger.LogInformation("[DUMMY] Start pressed, waiting for your first hit");
            }
        }

        /// <summary>Freezes the running dummy fight so its numbers stay on screen.</summary>
        public void StopDummyRun()
        {
            lock (lockObject)
            {
                if (DummyRun == DummyRunState.Running && dummyRunTargetId is { } id && targetEntries.TryGetValue(id, out var entry))
                    entry.CurrentSession?.Freeze();
                DummyRun = DummyRun == DummyRunState.Armed ? DummyRunState.Idle : DummyRunState.Stopped;
                logger.LogInformation("[DUMMY] Stop pressed");
            }
        }

        /// <summary>Timed mode takes every dummy hit; Start/Stop only takes hits on the run's dummy while it runs.</summary>
        private bool AcceptDummyHit(PlayerDamage damage)
        {
            if (settingsService.Current.DummyMode != DummyMode.StartStop) return true;
            switch (DummyRun)
            {
                case DummyRunState.Armed:
                    // Your first hit starts it (anyone's, if Hawkeye does not know who you are yet).
                    if (entityTracker.HasCurrentUser && !entityTracker.IsCurrentPlayer(damage.SourceEntity.Id)) return false;
                    dummyRunTargetId = damage.TargetEntity.Id;
                    DummyRun = DummyRunState.Running;
                    return true;
                case DummyRunState.Running:
                    return damage.TargetEntity.Id == dummyRunTargetId;
                default:
                    return false;
            }
        }

        private void OnSettingsChanged()
        {
            RefreshTrackedIds();
            var mode = settingsService.Current.DummyMode;
            if (mode == lastDummyMode) return;
            lastDummyMode = mode;
            lock (lockObject)
            {
                DummyRun = DummyRunState.Idle;
                dummyRunTargetId = null;
            }
        }

        public double GetPartyDps()
        {
            double totalDamage = PlayerStats.Sum(r => r.TotalDamage);
            var combatDuration = GetCombatDuration();
            return totalDamage / combatDuration.TotalSeconds;
        }

        public IReadOnlyList<PlayerDamage> GetPlayerCombatLog(long playerId)
        {
            lock (lockObject)
            {
                return GetActiveTargetSession()?.GetCombatLog(playerId) ?? [];
            }
        }

        public IReadOnlyCollection<SkillStats> GetPlayerSkillStats(long playerId)
        {
            lock (lockObject)
            {
                return GetActiveTargetSession()?.GetSkillStats(playerId) ?? [];
            }
        }

        public TimeSpan GetCombatDuration()
        {
            lock (lockObject)
            {
                return GetActiveTargetSession()?.GetCombatDuration() ?? TimeSpan.Zero;
            }
        }

        public Mob? GetActiveTargetInfo()
        {
            lock (lockObject) { return targetResolver.GetActiveTargetMob(); }
        }

       

        public void ProcessDamageEvent(PlayerDamage damageEvent)
        {
            try
            {
                lock (lockObject)
                {
                    var mob = entityTracker.GetTargetMob(damageEvent.TargetEntity.Id) ?? damageEvent.TargetEntity;
                    // Bosses and training dummies only (IsBoss is true for both); trash mobs never open a fight.
                    if (settingsService.Current.BossOnlyCapture && !mob.IsBoss) return;

                    if (entityTracker.IsSummon(damageEvent.SourceEntity.Id) &&
                        !ResolveSummonSource(damageEvent))
                        return;

                    if (mob.IsDummy && !AcceptDummyHit(damageEvent)) return;
                    if (!mob.IsDummy) NoteBossFighter(mob, damageEvent.SourceEntity.Id);

                    RouteToTargetEntry(damageEvent);

                    // Check all other entries for idle timeout on each new event
                    CheckIdleTimeouts(damageEvent.DateTime, excludeTargetId: damageEvent.TargetEntity.Id);

                    targetResolver.Update(targetEntries.Values, damageEvent.DateTime);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing damage event");
            }
        }

        public void ProcessBuffEvent(BuffEvent buffEvent)
        {
            try
            {
                lock (lockObject)
                {
                    RegisterTimedBuffEvent(buffEvent);
                    activeBuffBacklog.RemoveAll(b => b.AppliedAt.AddMilliseconds(b.DurationMs) <= buffEvent.AppliedAt);
                    activeBuffBacklog.Add(buffEvent);

                    foreach (var targetEntry in AllTargetEntries)
                    {
                        targetEntry.TryAddBuff(buffEvent);

                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing buff event");
            }
        }

        // ---- Working out which entity is you, from your own casts --------------------------------
        // Only used when Hawkeye missed the login/zone message that names you; once you are known
        // this does nothing. The cooldown message (0x3847) holds only YOUR skills, but the server
        // also re-sends the whole list regularly (with "0 ms left" for ready skills), so a cooldown
        // update alone proves nothing (I10). Evidence is a cooldown that STARTS (ready -> counting)
        // within EchoWindow of a cast of that skill by exactly one entity. That is one vote; the
        // guess is made only when one entity has VotesNeeded votes and clearly leads any rival.
        private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(2);
        private const int VotesNeeded = 2;
        private readonly List<(int EntityId, int SkillId, DateTime At)> recentCasts = new();
        private readonly List<(int SkillId, DateTime At)> recentCooldownStarts = new();
        private readonly Dictionary<int, int> lastCooldownMs = new();   // skill -> ms left last time
        private readonly Dictionary<int, int> identityVotes = new();    // entity -> votes

        private void TryIdentifyCurrentPlayer(DateTime now)
        {
            if (entityTracker.HasCurrentUser)
            {
                recentCasts.Clear();
                recentCooldownStarts.Clear();
                identityVotes.Clear();
                return;
            }

            recentCasts.RemoveAll(c => now - c.At > EchoWindow);
            recentCooldownStarts.RemoveAll(c => now - c.At > EchoWindow);

            for (int i = recentCooldownStarts.Count - 1; i >= 0; i--)
            {
                var start = recentCooldownStarts[i];
                var casters = recentCasts.Where(c => c.SkillId == start.SkillId).Select(c => c.EntityId).Distinct().ToList();
                if (casters.Count == 0) continue;        // the cast may still be on its way
                recentCooldownStarts.RemoveAt(i);        // each cooldown start is one piece of evidence
                if (casters.Count != 1) continue;        // two casters of the same skill: no vote

                int entity = casters[0];
                identityVotes[entity] = identityVotes.GetValueOrDefault(entity) + 1;
            }

            if (identityVotes.Count == 0) return;
            var ranked = identityVotes.OrderByDescending(v => v.Value).ToList();
            int best = ranked[0].Value, rival = ranked.Count > 1 ? ranked[1].Value : 0;
            if (best < VotesNeeded || best < rival + VotesNeeded) return;

            if (entityTracker.MarkCurrentPlayer(ranked[0].Key))
                logger.LogInformation("[ME] Identified you as entity {EntityId} from {Votes} casts that started a cooldown (rival best {Rival})",
                    ranked[0].Key, best, rival);
        }

        // Snapshot of the tracked skill ids. The packet thread reads it; the UI thread may be editing
        // settings at the same moment, so the set is rebuilt on every change and swapped in whole.
        private volatile HashSet<int> trackedIds = new();

        private void RefreshTrackedIds() =>
            trackedIds = settingsService.Current.TrackedSkills.Select(t => t.SkillId).ToHashSet();

        private bool IsTracked(int skillId) => trackedIds.Contains(skillId);

        public void RegisterSkillCastEvent(SkillCastEvent skillCastEvent)
        {
            var now = DateTime.UtcNow;
            recentCasts.Add((skillCastEvent.EntityId, skillCastEvent.SkillId, now));
            TryIdentifyCurrentPlayer(now);

            bool mine = entityTracker.IsCurrentPlayer(skillCastEvent.EntityId);
            if (IsTracked(skillCastEvent.SkillId))
                logger.LogInformation("[TRACK] cast {Name} ({SkillId}) by {EntityId} (you: {Mine}) cooldown {Ms} ms",
                    skillCastEvent.Name, skillCastEvent.SkillId, skillCastEvent.EntityId, mine, (int)skillCastEvent.Cd.TotalMilliseconds);
            if (!mine) return;

            skillCdEventTracker.Register(new TimedEvent()
            {
                Name = skillCastEvent.Name,
                IconUrl = skillCastEvent.IconUrl ?? string.Empty,
                Duration = skillCastEvent.Cd,
                Id = (uint)skillCastEvent.SkillId
            });
        }

        public void RegisterSkillCdUpdateEvent(TimedEvent skillCdEvent)
        {
            var now = DateTime.UtcNow;
            int skill = (int)skillCdEvent.Id;
            int ms = (int)skillCdEvent.Duration.TotalMilliseconds;
            // A start: it was ready (or never seen) and is now counting, or it jumped back up (re-cast).
            bool started = ms > 0 && (!lastCooldownMs.TryGetValue(skill, out var previous) || previous == 0 || ms > previous + 500);
            lastCooldownMs[skill] = ms;
            if (started) recentCooldownStarts.Add((skill, now));
            TryIdentifyCurrentPlayer(now);

            if (IsTracked((int)skillCdEvent.Id))
                logger.LogInformation("[TRACK] cooldown update {Name} ({SkillId}) {Ms} ms left",
                    skillCdEvent.Name, skillCdEvent.Id, (int)skillCdEvent.Duration.TotalMilliseconds);
            skillCdEventTracker.Register(skillCdEvent);
        }

        private void RegisterTimedBuffEvent(BuffEvent buffEvent)
        {
            bool tracked = IsTracked(buffEvent.BuffId);
            bool onMe = entityTracker.IsCurrentPlayer(buffEvent.EntityId);
            bool byMe = entityTracker.IsCurrentPlayer(buffEvent.CasterId);
            bool meKnown = entityTracker.HasCurrentUser;

            if (tracked)
                logger.LogInformation("[TRACK] buff {Name} ({BuffId}) on {EntityId} by {CasterId} for {Ms} ms (on you: {OnMe}, by you: {ByMe}, you known: {Known})",
                    buffEvent.BuffName, buffEvent.BuffId, buffEvent.EntityId, buffEvent.CasterId, buffEvent.DurationMs, onMe, byMe, meKnown);

            else if (byMe)
                logger.LogInformation("[SEEN] buff you cast: {Name} ({BuffId}) on {EntityId} for {Ms} ms",
                    buffEvent.BuffName, buffEvent.BuffId, buffEvent.EntityId, buffEvent.DurationMs);

            // Buffs on you, plus anything you put on something else (e.g. a mark on the boss).
            // Until Hawkeye knows who you are, a TRACKED buff is accepted from anyone rather than ignored.
            bool accept = onMe || byMe || (tracked && !meKnown);
            if (!accept) return;
            buffEventTracker.Register(new TimedEvent()
            {
                Name = buffEvent.BuffName,
                IconUrl = buffEvent.BuffIcon ?? string.Empty,
                Duration = TimeSpan.FromMilliseconds(buffEvent.DurationMs),
                Id = (uint)buffEvent.BuffId
            });
        }

        public void Reset()
        {
            lock (lockObject) { ResetInternal(); }
        }

        public void CompleteActiveSessions(DateTime? at = null)
        {
            lock (lockObject)
            {
                var completedAt = at ?? DateTime.Now;
                foreach (var entry in targetEntries.Values)
                {
                    entry.CompleteActiveSession();
                }

                targetResolver.Update(targetEntries.Values, completedAt);
            }
        }

        private IReadOnlyCollection<TargetEntry> AllTargetEntries
        {
            get { lock (lockObject) { return targetEntries.Values.ToList(); } }
        }


        private void RouteToTargetEntry(PlayerDamage damageEvent)
        {
            var entry = targetEntries.GetOrAdd(
                damageEvent.TargetEntity.Id,
                id => new TargetEntry(id, entityTracker, settingsService));

            var previousSession = entry.CurrentSession;
            entry.AddDamage(damageEvent);
            var currentSession = entry.CurrentSession;

            if (!ReferenceEquals(previousSession, currentSession) && currentSession is not null)
            {
                ReplayPreCombatBuffs(currentSession);
            }
        }

        private void ReplayPreCombatBuffs(TargetCombatSession session)
        {
            var sessionStart = session.SessionStart;
            foreach (var buff in activeBuffBacklog)
            {
                if (buff.AppliedAt >= sessionStart)
                    continue;

                var buffEnd = buff.AppliedAt.AddMilliseconds(buff.DurationMs);
                if (buffEnd <= sessionStart)
                    continue;

                session.ProcessBuffEvent(buff);
            }
        }


        private void CheckIdleTimeouts(DateTime now, int excludeTargetId)
        {
            foreach (var (id, entry) in targetEntries)
            {
                if (id == excludeTargetId) continue;
                entry.CheckIdleTimeout(now);
            }
        }

        private bool ResolveSummonSource(PlayerDamage damageEvent)
        {
            var ownerId = entityTracker.GetSummonOwner(damageEvent.SourceEntity.Id);
            if (ownerId is null)
            {
                logger.LogError("Summon owner not found for summon entity {SummonId}", damageEvent.SourceEntity.Id);
                return false;
            }
            damageEvent.SourceSummon = new Player
            {
                CharacterClass = damageEvent.SourceEntity.CharacterClass,
                Icon = damageEvent.SourceEntity.Icon,
                Name = damageEvent.SourceEntity.Name,
                Id = damageEvent.SourceEntity.Id,
            };
            damageEvent.SourceEntity = new Player
            {
                CharacterClass = damageEvent.SourceEntity.CharacterClass,
                Icon = damageEvent.SourceEntity.Icon,
                Name = damageEvent.SourceEntity.Name,
                Id = ownerId.Value,
            };
            return true;
        }

        private void ResetInternal()
        {
            foreach (var entry in targetEntries.Values)
                entry.Reset();
            targetEntries.Clear();
            targetResolver.Reset();
            latestPlayerStatSnapshot = null;
            activeBuffBacklog.Clear();
        }

        private void OnSummonRegistered(int summonId, int ownerId)
        {
            lock (lockObject)
            {
                int totalTransferred = 0;
                int entriesAffected  = 0;

                foreach (var (targetId, entry) in targetEntries)
                {
                    if (entry.CurrentSession is not { } current) continue;

                    int result = current.TransferSummonDamage(summonId, ownerId);
                    if (result == 0)
                    {
                        logger.LogWarning(
                            "Summon late-registration: summon {SummonId} found in target entry {TargetId} but owner player entity {OwnerId} is unknown. Skipping transfer.",
                            summonId, targetId, ownerId);
                    }
                    else if (result > 0)
                    {
                        totalTransferred += result;
                        entriesAffected++;
                        logger.LogInformation(
                            "Summon late-registration: transferred {Count} hit(s) from summon {SummonId} to owner {OwnerId} in target entry {TargetId}.",
                            result, summonId, ownerId, targetId);
                    }
                }

                if (totalTransferred == 0)
                    logger.LogDebug(
                        "Summon late-registration: summon {SummonId} registered with owner {OwnerId} — no prior damage hits found to transfer.",
                        summonId, ownerId);
                else
                    logger.LogInformation(
                        "Summon late-registration complete: summon {SummonId} → owner {OwnerId}. Total hits transferred: {Total} across {Entries} target entry(ies).",
                        summonId, ownerId, totalTransferred, entriesAffected);
            }
        }
    }
}