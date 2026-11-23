using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.Services.Entity
{
    /// <summary>
    /// Training dummies Hawkeye has seen, by entity id, kept in %AppData%\Hawkeye\known-dummies.json (v0.2.23).
    ///
    /// Why (owner, 2026-11-23: "it used to capture dummies before, but doesn't now"): a mob's type only comes in the
    /// message that makes it appear (0x3641). When Hawkeye starts while you already stand at a dummy (an update
    /// restart mid-game), it never sees that message, takes the dummy for an unknown trash mob, and the boss-only rule
    /// drops every hit. Dummies are fixed NPCs that keep their entity id: in the owner's recordings the same two
    /// dummies had the same ids on 2026-11-21 and 2026-11-22. So a dummy seen once is recognised from then on.
    /// Only dummies are kept: bosses and trash respawn with new ids.
    /// </summary>
    public sealed class KnownDummies
    {
        private readonly string? path;
        private readonly ILogger? logger;
        private readonly Lock gate = new();
        private readonly Dictionary<int, int> byEntity = new();

        public KnownDummies(string? path = null, ILogger? logger = null)
        {
            this.path = path;
            this.logger = logger;
            if (path is null || !File.Exists(path)) return;
            try
            {
                var saved = JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(path));
                if (saved is not null) foreach (var (id, code) in saved) byEntity[id] = code;
            }
            catch (Exception e) { logger?.LogWarning(e, "[DUMMY] could not read {Path}", path); }
        }

        public int Count { get { lock (gate) return byEntity.Count; } }

        /// <summary>A dummy appeared: remember its id. Saves only when something is new.</summary>
        public void Remember(int entityId, int mobCode)
        {
            lock (gate)
            {
                if (byEntity.TryGetValue(entityId, out var known) && known == mobCode) return;
                byEntity[entityId] = mobCode;
                Save();
            }
        }

        /// <summary>The mob code of a dummy seen before with this entity id, or 0.</summary>
        public int CodeOf(int entityId)
        {
            lock (gate) return byEntity.GetValueOrDefault(entityId);
        }

        /// <summary>Like <see cref="CodeOf"/>, for a dummy met without its appear message: logs it.</summary>
        public int Recall(int entityId)
        {
            int code = CodeOf(entityId);
            if (code != 0) logger?.LogInformation("[DUMMY] entity {Id} is a dummy seen before (mob {Code}); its appear message was missed", entityId, code);
            return code;
        }

        private void Save()
        {
            if (path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(byEntity));
            }
            catch (Exception e) { logger?.LogWarning(e, "[DUMMY] could not save {Path}", path); }
        }
    }
}
