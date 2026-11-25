using System.Text.Json;
using Hawkeye.Engine.Services.History;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.Services.Skills
{
    /// <summary>
    /// Your skill levels, from the game's skill list (0x5100, <see cref="SkillListParser"/>), one list per class.
    /// Every fight saved to history gets your level per skill (<see cref="Stamp"/>), so fights can be compared when
    /// a skill's level differs (owner, 2026-11-25: "which is better at lvl 20 if can have only one").
    ///
    /// Kept in %AppData%\Hawkeye\skill-levels.json so a Hawkeye started mid-session still knows them; the game
    /// re-sends the list at every teleport anyway. Inside the arena every skill reads 20 (the arena evens levels out).
    /// </summary>
    public sealed class SkillLevels
    {
        private readonly object gate = new();
        private readonly string? filePath;
        private readonly ILogger? logger;
        private Dictionary<int, Dictionary<int, int>> byClass = new();

        public event EventHandler? Changed;

        public static string DefaultFilePath => Path.Combine(Settings.SettingsService.DataDirectory, "skill-levels.json");

        /// <param name="filePath">Where the levels are kept; null keeps them in memory only (tests).</param>
        public SkillLevels(string? filePath, ILogger? logger)
        {
            this.filePath = filePath;
            this.logger = logger;
            Load();
        }

        /// <summary>The base id the levels are kept under: the skill without its specialisation digits.</summary>
        public static int BaseId(long skillId) => (int)(skillId / 10000 * 10000);

        private static bool IsClassSkill(long id) => id >= 11_000_000 && id < 20_000_000;

        /// <summary>A new skill list arrived: it replaces the one for its class.</summary>
        public void OnSkillList(IReadOnlyList<SkillListParser.Entry> entries)
        {
            var levels = new Dictionary<int, int>();
            foreach (var e in entries)
            {
                if (!IsClassSkill(e.SkillId) || e.Level <= 0) continue;
                int id = BaseId(e.SkillId);
                levels[id] = Math.Max(levels.GetValueOrDefault(id), e.Level);
            }
            if (levels.Count == 0) return;
            int classId = levels.Keys.GroupBy(k => k / 1_000_000).OrderByDescending(g => g.Count()).First().Key;

            lock (gate)
            {
                if (byClass.TryGetValue(classId, out var old) && old.Count == levels.Count && !old.Except(levels).Any()) return;
                byClass[classId] = levels;
                Save();
            }
            logger?.LogInformation("[SKILLS] class {Class}: {Count} skill levels", classId, levels.Count);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Your level of a skill, or null when not known.</summary>
        public int? LevelOf(long skillId)
        {
            if (!IsClassSkill(skillId)) return null;
            lock (gate)
            {
                return byClass.TryGetValue((int)(skillId / 1_000_000), out var levels) && levels.TryGetValue(BaseId(skillId), out var level)
                    ? level : null;
            }
        }

        /// <summary>Writes your current level into each of your skills in a fight (others' levels are never sent).</summary>
        public void Stamp(EncounterSnapshot snapshot)
        {
            foreach (var player in snapshot.Players.Where(p => p.IsUser))
                foreach (var skill in player.Skills)
                    if (LevelOf(skill.SkillId) is { } level) skill.Level = level;
        }

        private void Load()
        {
            if (filePath is null || !File.Exists(filePath)) return;
            try
            {
                byClass = JsonSerializer.Deserialize<Dictionary<int, Dictionary<int, int>>>(File.ReadAllText(filePath)) ?? new();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not read the saved skill levels");
            }
        }

        private void Save()
        {
            if (filePath is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(byClass));
                File.Move(tmp, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not save the skill levels");
            }
        }
    }
}
