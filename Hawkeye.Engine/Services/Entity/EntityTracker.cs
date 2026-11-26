using Hawkeye.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Hawkeye.Engine.Services.Entity
{
  
    public sealed class EntityTracker
    {
        // Fired when a summon is registered: (summonId, ownerId)
        public event Action<int, int>? SummonRegistered;

        public List<Player> PlayerEntities => sessionPlayers.Values.ToList();
        public List<Mob> TargetEntities => targetEntities.Values.ToList();
        public int PlayerEntityCount => sessionPlayers.Count;
        public int TargetEntityCount => targetEntities.Count;
        public int SummonCount => summons.Count;

        // Session id -> player. 
        private readonly ConcurrentDictionary<int, Player> sessionPlayers = new();

        // Global id -> player.
        private readonly ConcurrentDictionary<int, Player> globalPlayers = new();

        private readonly ConcurrentDictionary<int, Mob> targetEntities = new();
        private readonly ConcurrentDictionary<int, int> summons = new(); // summonId -> ownerId

        private string currentUserName = string.Empty;

        // Your entity id. Set by the login/zone player-info message, or worked out from your own casts
        // (CombatSessionManager: a cast followed by a matching cooldown update can only be yours).
        private int currentUserId;

        /// <summary>Members in your group from the last party message (0 = no group seen). Sizes the meter.</summary>
        public int PartySize { get; set; }

        // Everyone who was in a group with you since Hawkeye started (group lists, 0x9702). Replaced as a whole on each
        // change, so readers need no lock. Only added to: a group list that reads short (a partial update, a parse
        // miss) must not turn a group member's buff into a "debuff", and a past group member is no enemy.
        private HashSet<string> groupNames = new(StringComparer.Ordinal);

        /// <summary>Names from a group list (PartyProcessor).</summary>
        public void AddToGroup(IEnumerable<string> names)
        {
            var add = names.Where(n => !string.IsNullOrEmpty(n) && !groupNames.Contains(n)).ToList();
            if (add.Count > 0) groupNames = new HashSet<string>(groupNames.Concat(add), StringComparer.Ordinal);
        }

        /// <summary>
        /// You, someone who was in your group, or a summon of either. Used to tell a buff
        /// from a debuff on you: anything put on you by someone else counts as a debuff (v0.2.38).
        /// </summary>
        public bool IsFriendly(int entityId)
        {
            if (summons.TryGetValue(entityId, out var owner)) entityId = owner;
            if (IsCurrentPlayer(entityId)) return true;
            return sessionPlayers.TryGetValue(entityId, out var p) && p.IsIdentified && groupNames.Contains(p.Name);
        }

        private const int UnknownEntityHpThreshold = 1_000_000;

        /// <summary>Dummies seen before, by entity id, so one met without its appear message is still a dummy (v0.2.23).</summary>
        public KnownDummies? Dummies { get; set; }

        /// <summary>A mob met without its appear message: a dummy seen before gets its code back.</summary>
        private Mob NewUnseenMob(int entityId)
        {
            int code = sessionPlayers.ContainsKey(entityId) ? 0 : Dummies?.Recall(entityId) ?? 0;
            var mob = new Mob { Id = entityId, MobCode = code };
            targetEntities[entityId] = mob;
            return mob;
        }


        // ----- Targets (mobs) --------------------------------------------------

        public Mob GetOrCreateTargetEntity(int entityId)
        {
            if (targetEntities.TryGetValue(entityId, out var entity)) return entity;
            return NewUnseenMob(entityId);
        }

        public bool IsIdentifiedPlayer(int entityId) => sessionPlayers.TryGetValue(entityId, out var player) && player.IsIdentified;

        public bool IsCurrentPlayer(int entityId) =>
            (currentUserId != 0 && entityId == currentUserId)
            || (sessionPlayers.TryGetValue(entityId, out var player) && player.IsUser);

        /// <summary>True once Hawkeye knows which entity is you.</summary>
        public bool HasCurrentUser => currentUserId != 0 || sessionPlayers.Values.Any(p => p.IsUser);

        /// <summary>
        /// Marks an entity as you, used only when the login message was missed. Does nothing once you
        /// are known: a guess must never override what the server said (I10: it flipped you to
        /// another ranger). Returns true if this set you.
        /// </summary>
        public bool MarkCurrentPlayer(int entityId)
        {
            if (entityId == 0 || HasCurrentUser) return false;
            currentUserId = entityId;
            var player = GetOrCreateSessionPlayer(entityId);
            player.IsUser = true;
            if (player.IsIdentified) SetCurrentUser(player.Name);
            else TryNameUnnamedUser(player);
            return true;
        }

        /// <summary>
        /// Names you when the login message was missed (Hawkeye started mid-game, e.g. after an update): given your
        /// class id, returns the character you last played with that class, or null. Set by the Odyle tracker, which
        /// remembers your characters (v0.2.23; owner, 2026-11-23: the meter showed "Player_9377" instead of his name).
        /// </summary>
        public Func<int, string?>? GuessUserName { get; set; }

        private void TryNameUnnamedUser(Player player)
        {
            if (!player.IsUser || player.IsIdentified || GuessUserName is null || player.CharacterClass is null) return;
            var name = GuessUserName((int)player.CharacterClass.Id);
            if (!string.IsNullOrEmpty(name)) SetSessionPlayerName(player.Id, name, 0, isUser: true);
        }


        public bool UpdateTargetEntityHpCurrent(int entityId, long hpCurrent)
        {
            if (!targetEntities.TryGetValue(entityId, out var entity))
            {
                if (hpCurrent < UnknownEntityHpThreshold) return false;
                entity = NewUnseenMob(entityId);
            }

            entity.HpCurrent = hpCurrent;
            if (entity.HpCurrent > entity.HpTotal + 10_000_000)
            {
                entity.HpTotal = entity.HpCurrent;
            }
            return true;
        }

        public Mob CreateOrUpdateTargetEntity(int entityId, int mobCode, long hpTotal = 0)
        {
            if (mobCode != 0 && Dummies is not null && Hawkeye.Core.GameData.Services.GameDataProvider.Instance.IsDummy(mobCode))
                Dummies.Remember(entityId, mobCode);
            if (targetEntities.TryGetValue(entityId, out var entity))
            {
                entity.MobCode = mobCode;
                if (hpTotal > 0) entity.HpTotal = hpTotal;
                return entity;
            }

            var mob = new Mob
            {
                Id = entityId,
                MobCode = mobCode,
                HpTotal = hpTotal,
            };

            targetEntities[entityId] = mob;
            return mob;
        }

        public Core.Models.Entity? GetTargetEntity(int entityId) => targetEntities.GetValueOrDefault(entityId);

        public Mob? GetTargetMob(int entityId) => targetEntities.GetValueOrDefault(entityId);

        // ----- Flow 1: server sends a player tied to a session id --------------

        public Player GetOrCreateSessionPlayer(int sessionId, CharacterClass? characterClass = null)
        {
            if (sessionPlayers.TryGetValue(sessionId, out var player))
            {
                if (player.CharacterClass is null && characterClass is not null)
                {
                    player.CharacterClass = characterClass;
                    TryNameUnnamedUser(player);    // the class often arrives after you were worked out
                }
                return player;
            }

            player = new Player
            {
                Id = sessionId,
                Name = $"Player_{sessionId}",
                CharacterClass = characterClass,
            };

            sessionPlayers[sessionId] = player;
            CleanupUnidSessionPlayers();
            return player;
        }

        public void CleanupUnidSessionPlayers()
        {
            var toRemove = sessionPlayers.Where(r=>!r.Value.IsIdentified && r.Value.CreatedAt < DateTime.Now.AddMinutes(-10)).ToList();
            foreach (var kvp in toRemove) sessionPlayers.TryRemove(kvp.Key, out _);
        }

        public void SetSessionPlayerName(int sessionId, string name, int combatPower, string serverName = "", bool isUser = false)
        {
            if (sessionPlayers.TryGetValue(sessionId, out var existing) && existing.IsIdentified && existing.Name != name)
            {
                // Same session id, but it was already confirmed as a different
                // player - the id has been reused for someone else. Drop the
                // stale entity (and whatever it was linked to) and start fresh.
                sessionPlayers.TryRemove(sessionId, out _);
            }

            var player = GetOrCreateSessionPlayer(sessionId);

            player.Name = name;
            if(combatPower > 0) player.CombatPower = combatPower;
            if (!string.IsNullOrEmpty(serverName)) player.ServerName = serverName;
            player.IsIdentified = true;
            player.IsUser = isUser || name == currentUserName;

            if (isUser)
            {
                // The server's word wins over any earlier guess: nobody else stays marked as you.
                foreach (var other in sessionPlayers.Values)
                    if (other.Id != sessionId && other.Name != name) other.IsUser = false;
                currentUserId = sessionId;
                SetCurrentUser(name);
            }
            TryFallbackLinkByName(name);
        }

        public Player? GetPlayerEntity(int sessionId) => sessionPlayers.GetValueOrDefault(sessionId);

        /// <summary>Your combat power changed (0x3656: gear swapped, buffs). Ignored until Hawkeye knows who you are.</summary>
        public void SetUserCombatPower(int combatPower)
        {
            if (combatPower <= 0) return;
            foreach (var p in sessionPlayers.Values) if (p.IsUser) p.CombatPower = combatPower;
            foreach (var p in globalPlayers.Values) if (p.IsUser) p.CombatPower = combatPower;
        }

        // ----- Flow 2: server sends global player metadata, any time -----------

        public void RegisterOrUpdateGlobalPlayer(Player data)
        {
            var globalId = data.Id;

            if (!globalPlayers.TryGetValue(globalId, out var identity))
            {
                identity = new Player { Id = globalId };
                globalPlayers[globalId] = identity;
            }

            identity.Name = data.Name;
            identity.CharacterLevel = data.CharacterLevel;
            identity.CombatPower = data.CombatPower;
            identity.ServerId = data.ServerId;
            identity.ServerName = data.ServerName;
            if (data.CharacterClass != null) identity.CharacterClass = data.CharacterClass;
            identity.IsIdentified = true;
            identity.IsUser = identity.IsUser || identity.Name == currentUserName;

            PropagateIdentityToLinkedSessions(globalId);
            TryFallbackLinkByName(identity.Name);
        }

        // ----- Flow 3: packet carries both a global id and a session id --------

        public void LinkSessionToGlobalPlayer(int globalId, int sessionId)
        {
            var session = GetOrCreateSessionPlayer(sessionId);
            session.GlobalId = globalId;

            if (!globalPlayers.TryGetValue(globalId, out var identity))
            {
            
                identity = new Player
                {
                    Id = globalId,
                    Name = session.Name,
                    CharacterLevel = session.CharacterLevel,
                    CombatPower = session.CombatPower,
                    ServerId = session.ServerId,
                    ServerName = session.ServerName,
                    CharacterClass = session.CharacterClass,
                    IsUser = session.IsUser,
                    IsIdentified = session.IsIdentified,
                };
                globalPlayers[globalId] = identity;
            }

            ApplyIdentity(session, identity);
        }

        private void PropagateIdentityToLinkedSessions(int globalId)
        {
            if (!globalPlayers.TryGetValue(globalId, out var identity)) return;

            foreach (var session in sessionPlayers.Values)
            {
                if (session.GlobalId == globalId) ApplyIdentity(session, identity);
            }
        }

        private static void ApplyIdentity(Player session, Player identity)
        {   
            // Only a confirmed (server-given) name should ever overwrite a session's name.
            if (identity.IsIdentified)
            {
                session.Name = identity.Name;
                session.IsIdentified = true;
            }

            session.CharacterLevel = identity.CharacterLevel;
            session.CombatPower = identity.CombatPower;
            session.ServerId = identity.ServerId;
            if (!string.IsNullOrEmpty(identity.ServerName)) session.ServerName = identity.ServerName;
            session.CharacterClass ??= identity.CharacterClass;
            session.IsUser = session.IsUser || identity.IsUser;
            session.GlobalId = identity.Id;
        }

        // Link packets are sometimes missing entirely. As a fallback, if we
        // see the same name show up on both an unlinked session player and a
        // global identity, assume they're the same person and link them.
        private void TryFallbackLinkByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var session = sessionPlayers.Values.FirstOrDefault(p => p.GlobalId == null && p.Name == name);
            if (session == null) return;

            var identity = globalPlayers.Values.FirstOrDefault(p => p.Name == name);
            if (identity == null) return;

            session.GlobalId = identity.Id;
            ApplyIdentity(session, identity);
        }

        /// <summary>Raised when the game names the character you are playing (login, character switch).</summary>
        public event Action<string>? CurrentUserNamed;

        /// <summary>Your character's name once the game has said it; empty before.</summary>
        public string CurrentUserName => currentUserName;

        /// <summary>Your character's class id, if known (0 otherwise).</summary>
        public int CurrentUserClassId =>
            (int)(sessionPlayers.Values.FirstOrDefault(p => p.IsUser && p.Name == currentUserName)?.CharacterClass?.Id ?? 0);

        /// <summary>Your character's class name, if known (empty otherwise).</summary>
        public string CurrentUserClassName =>
            sessionPlayers.Values.FirstOrDefault(p => p.IsUser && p.Name == currentUserName)?.CharacterClass?.Name ?? "";

        private void SetCurrentUser(string name)
        {
            bool isNew = name != currentUserName;
            currentUserName = name;
            if (isNew && !string.IsNullOrEmpty(name) && !name.StartsWith("Player_")) CurrentUserNamed?.Invoke(name);

  
            foreach (var player in sessionPlayers.Values)
            {
                if (player.Name == name) player.IsUser = true;
            }
            foreach (var identity in globalPlayers.Values)
            {
                if (identity.Name == name) identity.IsUser = true;
            }
        }

        // ----- Summons -----------------------------------------------------------

        public void RegisterSummon(int summonId, int ownerId)
        {
            summons[summonId] = ownerId;
            SummonRegistered?.Invoke(summonId, ownerId);
        }

        public bool IsSummon(int entityId) => summons.ContainsKey(entityId);

        public int? GetSummonOwner(int summonId) => summons.TryGetValue(summonId, out var ownerId) ? ownerId : null;

        public void Clear()
        {
            // Players persist across a Clear() (e.g. a new pull in the same session);
            // only per-fight bookkeeping like summons resets.
            summons.Clear();
        }
    }
}
