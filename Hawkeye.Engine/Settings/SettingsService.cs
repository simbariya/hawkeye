using System.Text.Json;

namespace Hawkeye.Engine.Settings
{
    public interface ISettingsService
    {
        /// <summary>The live settings. Read freely; change them only through <see cref="Update"/>.</summary>
        HawkeyeSettings Current { get; }

        /// <summary>Apply a change, save to disk, then raise <see cref="Changed"/>.</summary>
        void Update(Action<HawkeyeSettings> change);

        event EventHandler? Changed;
    }

    public sealed class SettingsService : ISettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly Lock gate = new();
        private readonly string filePath;

        public HawkeyeSettings Current { get; }

        public event EventHandler? Changed;

        public SettingsService() : this(DefaultFilePath) { }

        public SettingsService(string filePath)
        {
            this.filePath = filePath;
            Current = Load(filePath);
        }

        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hawkeye");

        public static string DefaultFilePath => Path.Combine(DataDirectory, "settings.json");

        public void Update(Action<HawkeyeSettings> change)
        {
            lock (gate)
            {
                change(Current);
                Save();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static HawkeyeSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<HawkeyeSettings>(File.ReadAllText(path), JsonOptions) ?? new HawkeyeSettings();
            }
            catch
            {
                // Corrupt file: start from defaults rather than crash. The bad file is overwritten on the next save.
            }
            return new HawkeyeSettings();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tmp, filePath, overwrite: true);
            }
            catch
            {
                // Disk full / locked file: keep running with the in-memory settings.
            }
        }
    }
}
