using System.Text.Json;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Engine.Services.Arena
{
    /// <summary>An opponent in a saved arena match.</summary>
    public sealed record ArenaRecordFoe(string Name, int Server, int Rating, int ClassId, string ClassName);

    /// <summary>One finished arena match, as kept in the arena history.</summary>
    public sealed record ArenaRecord(
        Guid Id,
        DateTime At,
        string Character,
        bool Won,
        int OurKills,
        int TheirKills,
        int? RatingBefore,
        int RatingAfter,
        IReadOnlyList<ArenaRecordFoe> Opponents)
    {
        public int RatingChange => RatingBefore is { } before ? RatingAfter - before : 0;

        /// <summary>"1v1", "2v2"...: from the number of opponents.</summary>
        public string Mode => Opponents.Count > 0 ? $"{Opponents.Count}v{Opponents.Count}" : "";

        public static ArenaRecord From(ArenaMatch m, string character) => new(
            Guid.NewGuid(), m.At, character, m.Won == true, m.OurKills, m.TheirKills, m.RatingBefore, m.RatingAfter ?? 0,
            m.Enemies.Select(e => new ArenaRecordFoe(e.Name, e.Server, e.Rating, e.ClassId, e.ClassName)).ToList());
    }

    /// <summary>
    /// Every finished arena match, newest first, saved as it ends in %AppData%\Hawkeye\arena-matches.json (v0.2.42;
    /// owner, 2026-11-27: "Add Arena history, that autosaves into a separate tab, so after a version update I do not
    /// lose my stats of the arena"). The file sits with the settings, outside the program folder, so an update never
    /// touches it. Kept in full; you delete what you don't want. Saved like the PvP history: off the caller's thread,
    /// one save after another, through a temp file.
    /// </summary>
    public sealed class ArenaHistory
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly string path;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private List<ArenaRecord> records = new();
        private Task lastSave = Task.CompletedTask;

        public event EventHandler? Changed;

        // The service setup can fill only this one (the other needs a path string): no tie, see I15.
        public ArenaHistory(ILogger<ArenaHistory> logger) : this(Path.Combine(SettingsService.DataDirectory, "arena-matches.json"), logger) { }

        public ArenaHistory(string path, ILogger? logger = null)
        {
            this.path = path;
            this.logger = logger ?? NullLogger.Instance;
            try
            {
                if (File.Exists(path))
                    records = (JsonSerializer.Deserialize<List<ArenaRecord>>(File.ReadAllText(path)) ?? new())
                        .OrderByDescending(r => r.At).ToList();
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "[ARENA] could not read {Path}; starting an empty arena history", path);
            }
        }

        public IReadOnlyList<ArenaRecord> Records { get { lock (gate) return records.ToList(); } }

        public void Add(ArenaRecord record)
        {
            lock (gate)
            {
                records.Insert(0, record);
                QueueSave();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Deletes several matches with one save (History > Arena > Select).</summary>
        public void Delete(IReadOnlyCollection<Guid> ids)
        {
            lock (gate)
            {
                if (records.RemoveAll(r => ids.Contains(r.Id)) == 0) return;
                QueueSave();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Waits for every save started so far (call before the app exits).</summary>
        public Task FlushAsync() { lock (gate) return lastSave; }

        private void QueueSave()
        {
            var snapshot = records.ToList();
            lastSave = lastSave.ContinueWith(_ => Save(snapshot), TaskScheduler.Default);
        }

        private void Save(List<ArenaRecord> snapshot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Json));
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ARENA] could not save {Path}", path);
            }
        }
    }
}
