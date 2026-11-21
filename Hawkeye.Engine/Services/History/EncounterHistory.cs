using System.Text.Json;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Engine.Services.History
{
    public interface IEncounterHistory
    {
        /// <summary>Newest first, at most <see cref="EncounterHistory.MaxRecent"/>.</summary>
        IReadOnlyList<EncounterSnapshot> Recent { get; }

        /// <summary>Kept forever, newest first.</summary>
        IReadOnlyList<EncounterSnapshot> Saved { get; }

        /// <summary>Your most recent fight (recent list), or null.</summary>
        EncounterSnapshot? LastOwn { get; }

        void Add(EncounterSnapshot encounter);
        EncounterSnapshot? Find(Guid id);

        /// <summary>Copies a recent fight into the saved folder (no-op if already saved).</summary>
        void KeepForever(Guid id);

        /// <summary>Removes a fight from the saved folder.</summary>
        void DeleteSaved(Guid id);

        /// <summary>Waits until every file write started so far has finished (call before the app exits).</summary>
        Task FlushAsync();

        event EventHandler? Changed;
    }

    /// <summary>
    /// The last 50 fights you took part in, plus any you chose to keep forever. One JSON file per fight:
    /// %AppData%\Hawkeye\history\*.json (recent, oldest deleted past 50) and history\saved\*.json (never deleted
    /// by Hawkeye). Thread-safe: the packet thread adds, the UI reads. Files are written off the packet thread.
    /// </summary>
    public sealed class EncounterHistory : IEncounterHistory
    {
        public const int MaxRecent = 50;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        private readonly string recentDir;
        private readonly string savedDir;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private List<EncounterSnapshot> recent = new();
        private List<EncounterSnapshot> saved = new();
        private readonly List<Task> pendingWrites = new();

        public event EventHandler? Changed;

        public EncounterHistory(ILogger<EncounterHistory> logger) : this(Path.Combine(SettingsService.DataDirectory, "history"), logger) { }

        public EncounterHistory(string directory, ILogger? logger = null)
        {
            recentDir = directory;
            savedDir = Path.Combine(directory, "saved");
            this.logger = logger ?? NullLogger.Instance;
            recent = Load(recentDir).Take(MaxRecent).ToList();
            saved = Load(savedDir).ToList();
        }

        public IReadOnlyList<EncounterSnapshot> Recent { get { lock (gate) return recent.ToList(); } }
        public IReadOnlyList<EncounterSnapshot> Saved { get { lock (gate) return saved.ToList(); } }

        public EncounterSnapshot? LastOwn
        {
            get { lock (gate) return recent.FirstOrDefault(e => e.You is not null); }
        }

        public EncounterSnapshot? Find(Guid id)
        {
            lock (gate) return recent.FirstOrDefault(e => e.Id == id) ?? saved.FirstOrDefault(e => e.Id == id);
        }

        public void Add(EncounterSnapshot encounter)
        {
            List<EncounterSnapshot> dropped;
            lock (gate)
            {
                recent.Insert(0, encounter);
                dropped = recent.Skip(MaxRecent).ToList();
                if (dropped.Count > 0) recent = recent.Take(MaxRecent).ToList();
            }

            var write = Task.Run(() =>
            {
                Write(recentDir, encounter);
                foreach (var old in dropped) TryDelete(FilePath(recentDir, old));
            });
            lock (gate)
            {
                pendingWrites.RemoveAll(t => t.IsCompleted);
                pendingWrites.Add(write);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void KeepForever(Guid id)
        {
            EncounterSnapshot? copy;
            lock (gate)
            {
                var source = recent.FirstOrDefault(e => e.Id == id);
                if (source is null || saved.Any(e => e.Id == id)) return;
                source.IsSaved = true;
                copy = Clone(source);
                saved.Insert(0, copy);
            }
            Write(savedDir, copy);
            Write(recentDir, Find(id)!); // keep the "saved" mark on the recent copy too
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void DeleteSaved(Guid id)
        {
            EncounterSnapshot? gone;
            lock (gate)
            {
                gone = saved.FirstOrDefault(e => e.Id == id);
                if (gone is null) return;
                saved.Remove(gone);
                var inRecent = recent.FirstOrDefault(e => e.Id == id);
                if (inRecent is not null) inRecent.IsSaved = false;
            }
            TryDelete(FilePath(savedDir, gone));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task FlushAsync()
        {
            Task[] waiting;
            lock (gate) waiting = pendingWrites.ToArray();
            return Task.WhenAll(waiting);
        }

        // ------------------------------------------------------------------ files

        private static string FilePath(string dir, EncounterSnapshot e)
        {
            var safe = new string(e.TargetName.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            if (safe.Length > 40) safe = safe[..40];
            return Path.Combine(dir, $"{e.StartedAt:yyyyMMdd-HHmmss}_{safe}_{e.Id.ToString("N")[..8]}.json");
        }

        private void Write(string dir, EncounterSnapshot e)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var path = FilePath(dir, e);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(e, Json));
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not save fight history to {Dir}", dir);
            }
        }

        private IEnumerable<EncounterSnapshot> Load(string dir)
        {
            if (!Directory.Exists(dir)) return [];
            var list = new List<EncounterSnapshot>();
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var e = JsonSerializer.Deserialize<EncounterSnapshot>(File.ReadAllText(file), Json);
                    if (e is not null) list.Add(e);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skipped unreadable history file {File}", file);
                }
            }
            return list.OrderByDescending(e => e.StartedAt);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }

        private static EncounterSnapshot Clone(EncounterSnapshot e) =>
            JsonSerializer.Deserialize<EncounterSnapshot>(JsonSerializer.Serialize(e, Json), Json)!;
    }
}
