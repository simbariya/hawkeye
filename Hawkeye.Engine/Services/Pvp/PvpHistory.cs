using System.Text.Json;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Engine.Services.Pvp
{
    public enum PvpEventKind { Kill, Death, Assist }

    /// <summary>
    /// Who a kill, death or assist was about: the victim for kills and assists, the killer for deaths. Class and
    /// combat power only when known (class once they used a skill in view; combat power: not read for others yet).
    /// Server id as the game sends it (global ids such as 1304 are not in ServerMap); 0 when unknown.
    /// </summary>
    public sealed record PvpEvent(
        DateTime At,
        PvpEventKind Kind,
        string Name,
        string Legion,
        int ServerId,
        int ClassId,
        string ClassName,
        long CombatPower);

    /// <summary>One ended PvP session (owner, 2026-11-21: "end session button in pvp, saves in pvp history").</summary>
    public sealed record PvpSession(
        Guid Id,
        DateTime StartedAt,
        DateTime EndedAt,
        string Character,
        int Kills,
        int Deaths,
        int Assists,
        long Ap,
        long Rank,
        IReadOnlyList<PvpEvent> Events)
    {
        public TimeSpan Duration => EndedAt - StartedAt;
    }

    /// <summary>
    /// Every PvP session you ended, newest first, in one small file: %AppData%\Hawkeye\pvp-sessions.json. Kept in
    /// full (a session is a few numbers and names); you delete what you don't want. Saved off the caller's thread,
    /// one save after another, through a temp file so a crash mid-write never loses the list.
    /// </summary>
    public sealed class PvpHistory
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly string path;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private List<PvpSession> sessions = new();
        private Task lastSave = Task.CompletedTask;

        public event EventHandler? Changed;

        // The service setup can fill only this one (the other needs a path string): no tie, see I15.
        public PvpHistory(ILogger<PvpHistory> logger) : this(Path.Combine(SettingsService.DataDirectory, "pvp-sessions.json"), logger) { }

        public PvpHistory(string path, ILogger? logger = null)
        {
            this.path = path;
            this.logger = logger ?? NullLogger.Instance;
            try
            {
                if (File.Exists(path))
                    sessions = (JsonSerializer.Deserialize<List<PvpSession>>(File.ReadAllText(path)) ?? new())
                        .OrderByDescending(s => s.EndedAt).ToList();
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "[PVP] could not read {Path}; starting an empty PvP history", path);
            }
        }

        public IReadOnlyList<PvpSession> Sessions { get { lock (gate) return sessions.ToList(); } }

        public PvpSession? Find(Guid id) { lock (gate) return sessions.FirstOrDefault(s => s.Id == id); }

        public void Add(PvpSession session)
        {
            lock (gate)
            {
                sessions.Insert(0, session);
                QueueSave();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(Guid id) => Delete([id]);

        /// <summary>Deletes several sessions with one save (History > PvP > Select; owner 2026-11-22: clear test sessions).</summary>
        public void Delete(IReadOnlyCollection<Guid> ids)
        {
            lock (gate)
            {
                if (sessions.RemoveAll(s => ids.Contains(s.Id)) == 0) return;
                QueueSave();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Waits for every save started so far (call before the app exits).</summary>
        public Task FlushAsync() { lock (gate) return lastSave; }

        private void QueueSave()
        {
            var snapshot = sessions.ToList();
            lastSave = lastSave.ContinueWith(_ => Save(snapshot), TaskScheduler.Default);
        }

        private void Save(List<PvpSession> snapshot)
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
                logger.LogWarning(ex, "[PVP] could not save {Path}", path);
            }
        }
    }
}
