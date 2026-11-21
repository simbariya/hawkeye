using System.Text.Json.Serialization;

namespace Hawkeye.Engine.Settings
{
    /// <summary>What a tracked skill icon follows.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TrackMode
    {
        /// <summary>Bright when ready, dark with a countdown while on cooldown.</summary>
        Cooldown,

        /// <summary>Bright with a countdown while the buff is up, dim with a red frame when it has run out.</summary>
        Buff,
    }

    /// <summary>How a fight on a training dummy is measured.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DummyMode
    {
        /// <summary>Starts on your first hit and stops by itself after DummyMinutes.</summary>
        Timed,

        /// <summary>You press Start on the meter; it counts from your next hit until you press Stop.</summary>
        StartStop,
    }

    public sealed class TrackedSkill
    {
        public int SkillId { get; set; }
        public TrackMode Mode { get; set; } = TrackMode.Cooldown;

        /// <summary>Screen position of the icon (null = not placed yet).</summary>
        public double? Left { get; set; }
        public double? Top { get; set; }
    }

    /// <summary>
    /// Everything Hawkeye remembers between runs. Saved as JSON in %AppData%\Hawkeye\settings.json.
    /// </summary>
    public sealed class HawkeyeSettings
    {
        // ---- meter window ----
        public double Opacity { get; set; } = 0.92;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }

        // ---- status line under the header ----
        public bool ShowPing { get; set; } = true;
        public bool ShowFps { get; set; } = true;
        public bool ShowCpu { get; set; } = true;
        public bool ShowRam { get; set; } = true;

        // ---- meter rows ----
        /// <summary>Show each player's total damage next to their DPS.</summary>
        public bool ShowTotalDamage { get; set; } = false;
        /// <summary>Show how many times each player died on this boss (all pulls since its last kill).</summary>
        public bool ShowDeaths { get; set; } = true;

        // ---- training dummy ----
        public DummyMode DummyMode { get; set; } = DummyMode.Timed;
        /// <summary>Length of a Timed dummy run: 1, 2 or 3 minutes.</summary>
        public int DummyMinutes { get; set; } = 1;

        // ---- skill / buff tracker ----
        public bool TrackerEnabled { get; set; } = true;
        public double TrackerIconSize { get; set; } = 56;
        /// <summary>False (default): icons show only during a boss or dummy fight (and while Settings is open). True: always.</summary>
        public bool TrackerAlwaysVisible { get; set; } = false;
        public List<TrackedSkill> TrackedSkills { get; set; } = new();

        // ---- engine behaviour (no UI; edit the file if you ever need to) ----
        /// <summary>Only open a fight for bosses / training dummies, not trash mobs.</summary>
        public bool BossOnlyCapture { get; set; } = true;
        /// <summary>Count pet and summon damage as the owner's.</summary>
        public bool GroupSummonDamage { get; set; } = true;
        /// <summary>Save raw game traffic to the Packets folder, for testing the decoder offline.</summary>
        public bool RecordPackets { get; set; } = false;
    }
}
