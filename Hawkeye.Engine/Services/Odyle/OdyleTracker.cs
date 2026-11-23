using System.Text.Json;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Engine.Services.Odyle
{
    /// <summary>One character's Odyle as last read from the game.</summary>
    public sealed class CharacterOdyle
    {
        public string Name { get; set; } = string.Empty;
        public int ClassId { get; set; }
        public int Current { get; set; }
        public int Pool { get; set; }
        /// <summary>When Hawkeye last read it from the game (local time).</summary>
        public DateTime VerifiedAt { get; set; }
    }

    /// <summary>
    /// Remembers every character's Odyle, read when you log in or switch character, in
    /// %AppData%\Hawkeye\characters.json. The game sends the Odyle message about 4 s BEFORE the message that
    /// names the character, so a reading waits (up to <see cref="MatchWindow"/>) for the name; a reading with no
    /// switch after it (a change while playing) belongs to the character you are already on.
    /// </summary>
    public sealed class OdyleTracker
    {
        private static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(20);
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly EntityTracker entities;
        private readonly ILogger logger;
        private readonly string path;
        private readonly Lock gate = new();
        private readonly Func<DateTime> now;
        private List<CharacterOdyle> characters = new();
        private (int Current, int Pool, DateTime At)? pending;
        private readonly Timer settleTimer;

        public event EventHandler? Changed;

        public OdyleTracker(EntityTracker entities, ILogger<OdyleTracker> logger)
            : this(entities, Path.Combine(SettingsService.DataDirectory, "characters.json"), logger, null) { }

        public OdyleTracker(EntityTracker entities, string path, ILogger? logger = null, Func<DateTime>? clock = null)
        {
            this.entities = entities;
            this.path = path;
            this.logger = logger ?? NullLogger.Instance;
            now = clock ?? (() => DateTime.Now);
            characters = Load();
            entities.CurrentUserNamed += OnCurrentUserNamed;
            entities.GuessUserName = GuessUserName;
            settleTimer = new Timer(_ => Settle(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// After a restart mid-game the game does not name you again: take the character you last played (latest
        /// reading) with that class. Null when none of your characters has that class.
        /// </summary>
        private string? GuessUserName(int classId)
        {
            if (classId == 0) return null;
            CharacterOdyle? last;
            lock (gate) last = characters.Where(c => c.ClassId == classId && c.Name.Length > 0).OrderByDescending(c => c.VerifiedAt).FirstOrDefault();
            if (last is null) return null;
            logger.LogInformation("[ME] named you {Name}: your last character of that class (the login message was missed)", last.Name);
            return last.Name;
        }

        /// <summary>Every character seen, most Odyle first.</summary>
        public IReadOnlyList<CharacterOdyle> Characters
        {
            get { lock (gate) return characters.OrderByDescending(c => c.Current).Select(Copy).ToList(); }
        }

        /// <summary>The character you are on, if Hawkeye has read its Odyle.</summary>
        public CharacterOdyle? Current
        {
            get
            {
                var name = entities.CurrentUserName;
                lock (gate) return characters.FirstOrDefault(c => c.Name == name) is { } c ? Copy(c) : null;
            }
        }

        /// <summary>Odyle's key, learned from the login message; the default until then.</summary>
        public int OdyleKey { get; private set; } = OdyleParser.DefaultKey;

        /// <summary>A live change (0x610C, e.g. a cube loot): it is the character you are on, right now.</summary>
        public void OnUpdate(int current, int pool)
        {
            var name = entities.CurrentUserName;
            if (string.IsNullOrEmpty(name) || name.StartsWith("Player_")) return;
            lock (gate) Store(name, current, pool, now());
        }

        /// <summary>The login/switch message (0x610B), with the key that marks Odyle in later updates.</summary>
        public void OnReading(int current, int pool, int key)
        {
            OdyleKey = key;
            OnReading(current, pool);
        }

        /// <summary>Called by the packet reader with a fresh reading.</summary>
        public void OnReading(int current, int pool)
        {
            lock (gate) pending = (current, pool, now());
            logger.LogInformation("[ODYLE] Read {Current} (+{Pool}), waiting for the character name", current, pool);
        }

        private void OnCurrentUserNamed(string name)
        {
            lock (gate)
            {
                if (pending is { } r && now() - r.At <= MatchWindow)
                {
                    Store(name, r.Current, r.Pool, r.At);
                    pending = null;
                }
            }
        }

        /// <summary>A reading with no character switch after it belongs to the character you are on.</summary>
        public void Settle()
        {
            FillInClass();
            lock (gate)
            {
                if (pending is not { } r || now() - r.At <= MatchWindow) return;
                var name = entities.CurrentUserName;
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("Player_")) Store(name, r.Current, r.Pool, r.At);
                pending = null;
            }
        }

        /// <summary>
        /// The class is often unknown at the moment of the reading (it is learned a little later), which left
        /// every saved character without its class icon. Fill it in as soon as it is known.
        /// </summary>
        private void FillInClass()
        {
            var name = entities.CurrentUserName;
            int classId = entities.CurrentUserClassId;
            if (classId == 0 || string.IsNullOrEmpty(name)) return;
            lock (gate)
            {
                var c = characters.FirstOrDefault(x => x.Name == name);
                if (c is null || c.ClassId == classId) return;
                c.ClassId = classId;
            }
            QueueSave();
            ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke(this, EventArgs.Empty));
        }

        private void Store(string name, int current, int pool, DateTime at)
        {
            var c = characters.FirstOrDefault(x => x.Name == name);
            if (c is null) characters.Add(c = new CharacterOdyle { Name = name });
            c.Current = current;
            c.Pool = pool;
            c.VerifiedAt = at;
            int classId = entities.CurrentUserClassId;
            if (classId != 0) c.ClassId = classId;
            logger.LogInformation("[ODYLE] {Name}: {Current} (+{Pool}) / {Cap}", name, current, pool, OdyleParser.Cap);
            QueueSave();
            ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke(this, EventArgs.Empty));
        }

        private List<CharacterOdyle> Load()
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<List<CharacterOdyle>>(File.ReadAllText(path), Json) ?? new()
                    : new();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read {Path}; starting an empty character list", path);
                return new();
            }
        }

        private readonly Lock saveGate = new();
        private Task lastSave = Task.CompletedTask;

        private void QueueSave()
        {
            lock (saveGate) lastSave = lastSave.ContinueWith(_ => Save(), TaskScheduler.Default);
        }

        /// <summary>Waits until the latest save has been written (call before the app exits).</summary>
        public Task FlushAsync()
        {
            lock (saveGate) return lastSave;
        }

        /// <summary>
        /// Writes the LATEST list, one save at a time. (Handing each save its own copy let two quick saves finish
        /// in the wrong order and leave the older list on disk; a test caught it.) Saves run one after another
        /// (QueueSave chains them), so Save takes no save lock of its own: holding one here while taking `gate`
        /// deadlocked against Store, which holds `gate` and queues a save.
        /// </summary>
        private void Save()
        {
            try
            {
                List<CharacterOdyle> list;
                lock (gate) list = characters.Select(Copy).ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not save {Path}", path);
            }
        }

        private static CharacterOdyle Copy(CharacterOdyle c) => new()
        {
            Name = c.Name, ClassId = c.ClassId, Current = c.Current, Pool = c.Pool, VerifiedAt = c.VerifiedAt,
        };
    }
}
