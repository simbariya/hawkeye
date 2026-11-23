using Hawkeye.Engine.Services.Entity;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.Services.Arena
{
    /// <summary>
    /// Arena mode (owner, 2026-11-23: "an arena mode ... on load show your opponent ... and track when they use
    /// defiance, so I know when it's on cooldown and I can use my stun skills"; "kinda like pvp mode that tracks
    /// your rank wins/losses in a session").
    ///
    /// A match starts with the roster (0xE319): who is on your team, who is not, everyone's rating. Kills (0xE325)
    /// give the score. The result (0xE328) gives your new rating: up is a win, down a loss. If the result is missed,
    /// the next standings (0xE32A) settle it from the wins / losses count. Defiance is every class's crowd-control
    /// breaker; an enemy's cast (0x3802) carries their real cooldown (51.3 s for the Sorcerer in the recording; the
    /// skill's listed cooldown is 60 s, used when the cast gives none).
    /// </summary>
    public sealed class ArenaTracker
    {
        public static readonly TimeSpan DefaultDefianceCooldown = TimeSpan.FromSeconds(60);
        public const string DefianceName = "Defiance";

        private readonly EntityTracker entities;
        private readonly ILogger<ArenaTracker>? logger;
        private readonly Func<DateTime> now;
        private readonly object gate = new();
        private readonly List<ArenaMatch> matches = new();
        private Dictionary<int, ArenaParser.Standing> standings = new();

        // One constructor only: two that the service setup could fill broke 0.2.9 (I15).
        public ArenaTracker(EntityTracker entities, ILogger<ArenaTracker>? logger = null, Func<DateTime>? clock = null)
        {
            this.entities = entities;
            this.logger = logger;
            now = clock ?? (() => DateTime.Now);
            StartedAt = now();
        }

        public event EventHandler? Changed;
        /// <summary>A match just loaded: the meter switches to the arena view.</summary>
        public event EventHandler? MatchStarted;

        /// <summary>The meter shows the arena view (switched on by hand, or by a match loading).</summary>
        public bool Showing
        {
            get => showing;
            set { showing = value; shownByMatch = false; }
        }
        private bool showing;
        private bool shownByMatch;   // switched on by a match loading: switched off again when you leave the arena

        public DateTime StartedAt { get; private set; }
        public int Wins { get; private set; }
        public int Losses { get; private set; }
        public int RatingChange { get; private set; }
        /// <summary>Your rating in the mode you last played (or the first standing seen).</summary>
        public int? Rating { get; private set; }

        /// <summary>The match running now, or the last one until you leave the arena.</summary>
        public ArenaMatch? Match { get; private set; }
        public bool InMatch => Match is { Won: null };
        public IReadOnlyList<ArenaMatch> Matches { get { lock (gate) return matches.ToList(); } }

        public void Reset()
        {
            lock (gate)
            {
                Wins = Losses = RatingChange = 0;
                matches.Clear();
                StartedAt = now();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnRoster(List<ArenaParser.Player> players)
        {
            lock (gate)
            {
                string me = entities.CurrentUserName;
                var mine = players.FirstOrDefault(p => !string.IsNullOrEmpty(me) && p.Name == me);
                int myTeam = mine.Name is null ? -1 : mine.Team;
                var match = new ArenaMatch(now(), mine.Name is null ? null : mine.Rating);
                foreach (var p in players)
                {
                    if (mine.Name is not null && p.Key == mine.Key) continue;
                    if (myTeam >= 0 && p.Team == myTeam) match.Allies.Add(p);
                    else match.Enemies.Add(new ArenaFoe(p.Key, p.Name, p.Server, p.Rating));
                }
                if (mine.Name is not null) match.OurKeys.Add(mine.Key);
                foreach (var a in match.Allies) match.OurKeys.Add(a.Key);
                Match = match;
                if (!showing) { showing = true; shownByMatch = true; }
                logger?.LogInformation("[ARENA] match: {Count} enemies, ratings {Ratings}", match.Enemies.Count,
                    string.Join(", ", match.Enemies.Select(e => e.Rating)));
            }
            MatchStarted?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnKill(ArenaParser.Kill kill)
        {
            lock (gate)
            {
                if (Match is not { Won: null } m) return;
                if (m.OurKeys.Contains(kill.Killer)) m.OurKills++;
                else if (m.OurKeys.Contains(kill.Victim) || m.Enemies.Any(e => e.Key == kill.Killer)) m.TheirKills++;
                else return;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnResult(int newRating)
        {
            lock (gate)
            {
                if (Match is not { Won: null } m) return;
                bool won = m.RatingBefore is { } before ? newRating > before : m.OurKills > m.TheirKills;
                Settle(m, won, newRating);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnStandings(List<ArenaParser.Standing> list)
        {
            lock (gate)
            {
                var old = standings;
                standings = list.ToDictionary(s => s.Mode);
                foreach (var s in list)
                {
                    if (!old.TryGetValue(s.Mode, out var was)) continue;
                    // A match whose result message was missed: the counts tell how it went.
                    if (Match is { Won: null } m && (s.Wins > was.Wins || s.Losses > was.Losses))
                        Settle(m, s.Wins > was.Wins, s.Rating);
                    else if (s.Rating != was.Rating) Rating = s.Rating;
                }
                Rating ??= list.Count > 0 ? list[0].Rating : null;
                // Leaving the arena (standings come with every zone change): an unfinished match (you left) is
                // dropped; the view goes back to what you had before, if the match had switched it on.
                if (old.Count > 0 && Match is not null)
                {
                    if (Match.Won is null) Match = null;
                    if (shownByMatch) { showing = false; shownByMatch = false; }
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Settle(ArenaMatch m, bool won, int newRating)
        {
            // Their class, for the Wins / Losses list (the game announced them as players when the match loaded).
            foreach (var e in m.Enemies)
            {
                var p = (e.EntityId != 0 ? entities.GetPlayerEntity(e.EntityId) : null)
                        ?? entities.PlayerEntities.FirstOrDefault(x => x.Name == e.Name);
                if (p?.CharacterClass is { } c) { e.ClassId = (int)c.Id; e.ClassName = c.Name; }
                if (p is not null && e.EntityId == 0) e.EntityId = p.Id;
            }
            m.Won = won;
            m.RatingAfter = newRating;
            if (won) Wins++; else Losses++;
            if (m.RatingBefore is { } before) RatingChange += newRating - before;
            Rating = newRating;
            matches.Add(m);
            logger?.LogInformation("[ARENA] {Result} {Ours}-{Theirs}, rating {Before} to {After}", won ? "won" : "lost",
                m.OurKills, m.TheirKills, m.RatingBefore, newRating);
        }

        /// <summary>Any skill cast (0x3802). Only an enemy's Defiance matters.</summary>
        public void OnSkillCast(int entityId, string skillName, TimeSpan cooldown)
        {
            if (skillName != DefianceName || entities.IsCurrentPlayer(entityId)) return;
            lock (gate)
            {
                // Only during a match: in the open Abyss other players' Defiance is cast too (seen in the recording).
                if (Match is not { Won: null } m) return;
                string name = entities.GetPlayerEntity(entityId)?.Name ?? "";
                if (m.Allies.Any(a => a.Name == name && name != "")) return;
                var cd = cooldown >= TimeSpan.FromSeconds(10) && cooldown <= TimeSpan.FromMinutes(5) ? cooldown : DefaultDefianceCooldown;
                var foe = m.Enemies.FirstOrDefault(e => e.EntityId == entityId)
                          ?? m.Enemies.FirstOrDefault(e => e.Name == name && name != "")
                          ?? (m.Enemies.Count == 1 && m.Enemies[0].EntityId == 0 ? m.Enemies[0] : null);
                if (foe is null)
                {
                    foe = new ArenaFoe(0, name == "" ? "Enemy" : name, 0, 0);
                    m.Enemies.Add(foe);
                }
                foe.EntityId = entityId;
                foe.DefianceAt = now();
                foe.DefianceCooldown = cd;
                logger?.LogInformation("[ARENA] {Name} used Defiance (cooldown {Cd:0.0} s)", foe.Name, cd.TotalSeconds);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>How long until this enemy's Defiance is back; zero when it is ready (or never used).</summary>
        public TimeSpan DefianceLeft(ArenaFoe foe)
        {
            if (foe.DefianceAt is not { } at) return TimeSpan.Zero;
            var left = at + foe.DefianceCooldown - now();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public sealed class ArenaMatch(DateTime at, int? ratingBefore)
    {
        public DateTime At { get; } = at;
        public int? RatingBefore { get; } = ratingBefore;
        public int? RatingAfter { get; set; }
        public bool? Won { get; set; }
        public int OurKills { get; set; }
        public int TheirKills { get; set; }
        public List<ArenaFoe> Enemies { get; } = new();
        public List<ArenaParser.Player> Allies { get; } = new();
        internal HashSet<ulong> OurKeys { get; } = new();
    }

    public sealed class ArenaFoe(ulong key, string name, int server, int rating)
    {
        public ulong Key { get; } = key;
        public string Name { get; } = name;
        public int Server { get; } = server;
        public int Rating { get; } = rating;
        public int EntityId { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public DateTime? DefianceAt { get; set; }
        public TimeSpan DefianceCooldown { get; set; } = ArenaTracker.DefaultDefianceCooldown;
    }
}
