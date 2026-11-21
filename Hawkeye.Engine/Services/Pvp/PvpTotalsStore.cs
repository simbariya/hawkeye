using System.Text.Json;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Engine.Services.Pvp
{
    /// <summary>
    /// Your last seen AP and rank point totals per character, kept across restarts in
    /// %AppData%\Hawkeye\pvp-totals.json. The game sends these totals only when they change, so without a remembered
    /// value the first kill after starting Hawkeye had nothing to compare against and went uncounted (owner,
    /// 2026-11-21: "is there a way to make first kill count?"). <see cref="PvpTracker"/> trusts a remembered value
    /// only for a jump one kill can explain; anything larger was earned while Hawkeye was closed.
    /// </summary>
    public sealed class PvpTotalsStore
    {
        public sealed class Totals
        {
            public long? Ap { get; set; }
            public long? Rank { get; set; }
            public DateTime SeenAt { get; set; }
        }

        private sealed class FileShape
        {
            public string Last { get; set; } = "";
            public Dictionary<string, Totals> Characters { get; set; } = new();
        }

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly string path;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private FileShape data = new();
        private Task lastSave = Task.CompletedTask;

        // The service setup can fill only this one (the other needs a path string): no tie, see I15.
        public PvpTotalsStore(ILogger<PvpTotalsStore> logger) : this(Path.Combine(SettingsService.DataDirectory, "pvp-totals.json"), logger) { }

        public PvpTotalsStore(string path, ILogger? logger = null)
        {
            this.path = path;
            this.logger = logger ?? NullLogger.Instance;
            try
            {
                if (File.Exists(path)) data = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "[PVP] could not read {Path}", path);
            }
        }

        /// <summary>The remembered totals for this character (or, if the name is not known yet, the last one played).</summary>
        public Totals? Get(string? character)
        {
            lock (gate)
            {
                var name = string.IsNullOrEmpty(character) ? data.Last : character;
                return name.Length > 0 && data.Characters.TryGetValue(name, out var t) ? t : null;
            }
        }

        public void Remember(string? character, long? ap = null, long? rank = null)
        {
            if (string.IsNullOrEmpty(character)) return;
            lock (gate)
            {
                if (!data.Characters.TryGetValue(character, out var t)) data.Characters[character] = t = new Totals();
                if (ap is not null) t.Ap = ap;
                if (rank is not null) t.Rank = rank;
                t.SeenAt = DateTime.Now;
                data.Last = character;
                var json = JsonSerializer.Serialize(data, Json);
                lastSave = lastSave.ContinueWith(_ => Save(json), TaskScheduler.Default);
            }
        }

        public Task FlushAsync() { lock (gate) return lastSave; }

        private void Save(string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[PVP] could not save {Path}", path);
            }
        }
    }
}
