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
        /// <summary>An arena match just ended with a result (not a battlefield): the arena history saves it.</summary>
        public event EventHandler<ArenaMatch>? MatchEnded;
        private ArenaMatch? justEnded;

        private void RaiseEnded()
        {
            ArenaMatch? m;
            lock (gate) { m = justEnded; justEnded = null; }
            if (m is not null) MatchEnded?.Invoke(this, m);
        }

        /// <summary>The meter shows the arena view (switched on by hand, or by a match loading).</summary>
        public bool Showing
        {
            get => showing;
            set { showing = value; shownByMatch = false; }
        }
        private bool showing;
        private bool shownByMatch;   // switched on by a match loading: switched off again when you leave the arena

        /// <summary>
        /// Enemy Defiance is tracked only in an arena match, never in a battlefield (owner, 2026-11-27: "ensure
        /// defiance only works in arena and not in battlefield as I don't wanna see defiance for 12 other people").
        /// A battlefield loads with the same roster message as an arena match; it is told apart by how many players
        /// cast skills in it (<see cref="BattlefieldCasters"/>). Until v0.2.42 it also needed PvP mode on (v0.2.36).
        /// </summary>
        public bool DefianceTracked => Match is { Won: null, IsBattlefield: false };

        /// <summary>
        /// More different players than this casting skills in one match makes it a battlefield. The owner's 17
        /// recorded 1v1 matches saw 3 to 8 casters each (you, the opponent, summons); a battlefield has dozens.
        /// </summary>
        public const int BattlefieldCasters = 20;

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

        // Your arena key ([u32 character id][u16 0][u16 server]), from 0xE343 as you enter the arena or from a result.
        // By key, not by name: after a restart mid-game Hawkeye may not know your name yet, and on 2026-11-23 that put
        // the owner on the enemy team, so a 3-2 win read "lost 0-5".
        private ulong? myKey;

        /// <summary>Your arena key (0xE343, sent as you enter the arena).</summary>
        public void OnOwnKey(ulong key)
        {
            lock (gate)
            {
                myKey = key;
                if (Match is { Won: null, MeKey: null } m) Assign(m);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnRoster(List<ArenaParser.Player> players)
        {
            lock (gate)
            {
                var match = new ArenaMatch(now(), null);
                match.Players.AddRange(players);
                Assign(match);
                Match = match;
                if (!showing) { showing = true; shownByMatch = true; }
                // Server ids logged so new servers can be named for profile links (shugo.gg). For a server with no
                // known name the opponent's name is logged too, so their profile can be looked up (the log stays local).
                logger?.LogInformation("[ARENA] match: {Count} enemies ({Foes}){Unknown}", match.Enemies.Count,
                    string.Join(", ", match.Enemies.Select(e => $"rating {e.Rating}, server {e.Server}"
                        + (Pvp.ShugoLinks.SlugFor(e.Server) is null && e.Server > 0 ? $" (unknown, \"{e.Name}\")" : ""))),
                    match.MeKey is null ? " (you not found yet)" : "");
            }
            MatchStarted?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Splits the roster into you, allies and enemies, and counts the kills again from the list.</summary>
        private void Assign(ArenaMatch m)
        {
            string me = entities.CurrentUserName;
            int mine = myKey is { } k ? m.Players.FindIndex(p => p.Key == k) : -1;
            if (mine < 0 && !string.IsNullOrEmpty(me)) mine = m.Players.FindIndex(p => p.Name == me);
            var old = m.Enemies.ToDictionary(e => e.Key);
            m.Enemies.Clear();
            m.Allies.Clear();
            m.OurKeys.Clear();
            if (mine >= 0)
            {
                var you = m.Players[mine];
                m.MeKey = you.Key;
                m.RatingBefore = you.Rating;
                m.OurKeys.Add(you.Key);
            }
            foreach (var p in m.Players)
            {
                if (m.MeKey == p.Key) continue;
                if (mine >= 0 && p.Team == m.Players[mine].Team) { m.Allies.Add(p); m.OurKeys.Add(p.Key); }
                else m.Enemies.Add(old.TryGetValue(p.Key, out var kept) ? kept : new ArenaFoe(p.Key, p.Name, p.Server, p.Rating));
            }
            Recount(m);
        }

        private static void Recount(ArenaMatch m)
        {
            m.OurKills = m.TheirKills = 0;
            if (m.MeKey is null) return;   // whose kill it was is not known yet
            foreach (var kill in m.Kills)
            {
                if (m.OurKeys.Contains(kill.Killer)) m.OurKills++;
                else m.TheirKills++;
            }
        }

        public void OnKill(ArenaParser.Kill kill)
        {
            lock (gate)
            {
                if (Match is not { Won: null } m) return;
                m.Kills.Add(kill);
                Recount(m);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnResult(int newRating, ulong? yourKey = null)
        {
            lock (gate)
            {
                if (Match is not { Won: null } m) return;
                if (yourKey is { } k && m.MeKey != k && m.Players.Any(p => p.Key == k))
                {
                    myKey = k;
                    Assign(m);
                }
                bool won = m.RatingBefore is { } before ? newRating > before : m.OurKills > m.TheirKills;
                Settle(m, won, newRating);
            }
            RaiseEnded();
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
            RaiseEnded();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Settle(ArenaMatch m, bool won, int newRating)
        {
            // A battlefield is not an arena match: not counted in wins, losses or the arena history.
            if (m.IsBattlefield) { Match = null; return; }
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
            justEnded = m;
            logger?.LogInformation("[ARENA] {Result} {Ours}-{Theirs}, rating {Before} to {After}", won ? "won" : "lost",
                m.OurKills, m.TheirKills, m.RatingBefore, newRating);
        }

        /// <summary>Any skill cast (0x3802). Counts the casters (a battlefield has many); only an enemy's Defiance shows.</summary>
        public void OnSkillCast(int entityId, string skillName, TimeSpan cooldown)
        {
            if (entities.IsCurrentPlayer(entityId)) return;
            bool changed = false;
            lock (gate)
            {
                // Only during a match: in the open Abyss other players' Defiance is cast too (seen in the recording).
                if (Match is not { Won: null } m) return;
                if (!m.IsBattlefield && m.Casters.Add(entityId) && m.Casters.Count > BattlefieldCasters)
                {
                    m.IsBattlefield = changed = true;
                    foreach (var e in m.Enemies) e.DefianceAt = null;
                    if (shownByMatch) { showing = false; shownByMatch = false; }
                    logger?.LogInformation("[ARENA] {Count} players cast skills: a battlefield, not an arena match (no Defiance)", m.Casters.Count);
                }
                if (skillName != DefianceName || m.IsBattlefield) goto done;
                if (entities.IsFriendly(entityId)) goto done;
                string name = entities.GetPlayerEntity(entityId)?.Name ?? "";
                if (m.Allies.Any(a => a.Name == name && name != "")) goto done;
                var cd = cooldown >= TimeSpan.FromSeconds(10) && cooldown <= TimeSpan.FromMinutes(5) ? cooldown : DefaultDefianceCooldown;
                // Only players on the match's enemy list: strangers are never added (until v0.2.42 they were, so a
                // battlefield filled up with everyone who cast Defiance).
                var foe = m.Enemies.FirstOrDefault(e => e.EntityId == entityId)
                          ?? m.Enemies.FirstOrDefault(e => e.Name == name && name != "")
                          ?? (name == "" && m.Enemies.Count(e => e.EntityId == 0) == 1 ? m.Enemies.First(e => e.EntityId == 0) : null);
                if (foe is null) goto done;
                foe.EntityId = entityId;
                foe.DefianceAt = now();
                foe.DefianceCooldown = cd;
                logger?.LogInformation("[ARENA] {Name} used Defiance (cooldown {Cd:0.0} s)", foe.Name, cd.TotalSeconds);
                changed = true;
            }
            done:
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
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
        public int? RatingBefore { get; set; } = ratingBefore;
        public ulong? MeKey { get; set; }
        public List<ArenaParser.Player> Players { get; } = new();
        public List<ArenaParser.Kill> Kills { get; } = new();
        public int? RatingAfter { get; set; }
        public bool? Won { get; set; }
        public int OurKills { get; set; }
        public int TheirKills { get; set; }
        public List<ArenaFoe> Enemies { get; } = new();
        public List<ArenaParser.Player> Allies { get; } = new();
        internal HashSet<ulong> OurKeys { get; } = new();
        /// <summary>Different players (not you) who cast a skill since the match loaded.</summary>
        internal HashSet<int> Casters { get; } = new();
        /// <summary>Too many casters for an arena (<see cref="ArenaTracker.BattlefieldCasters"/>): not kept as a match.</summary>
        public bool IsBattlefield { get; set; }
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
