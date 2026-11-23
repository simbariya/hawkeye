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

    /// <summary>The overall look, chosen in Settings (owner picked these three on 2026-11-21).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Look
    {
        /// <summary>Black glass slab, hairline bars, big thin numerals. The default.</summary>
        Monolith,
        /// <summary>Deep glass, bars fading to a glowing edge, a light drifting along the top.</summary>
        Aurora,
        /// <summary>Charcoal, hairline rules, vermilion accent, serif numerals.</summary>
        Graphite,
    }

    /// <summary>How a fight on a training dummy is measured.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    /// <summary>Where Theostone alerts show (v0.2.19): their own window you can drag anywhere, or over the meter.</summary>
    public enum TheostonePlace
    {
        Screen,
        Meter,
    }

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

        /// <summary>
        /// Unticked: no icon and no timers, but it stays in the list with its mode and position (owner, 2026-11-21:
        /// switch skills off on another character instead of deleting them). Older settings files load as active.
        /// </summary>
        public bool Active { get; set; } = true;

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
        public Look Look { get; set; } = Look.Monolith;
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
        /// <summary>Meter height follows your group: room for every member, up to 5 rows. Off: resize it yourself.</summary>
        public bool AutoHeight { get; set; } = true;
        /// <summary>Show each player's total damage next to their DPS.</summary>
        public bool ShowTotalDamage { get; set; } = false;
        /// <summary>Show how many times each player died on this boss (all pulls since its last kill).</summary>
        public bool ShowDeaths { get; set; } = true;

        /// <summary>Flash a small alert on the meter when your Theostone procs (v0.2.17).</summary>
        public bool TheostoneAlertsYours { get; set; } = true;

        /// <summary>Flash a small red alert on the meter when someone's Theostone hits you (v0.2.17).</summary>
        public bool TheostoneAlertsOnYou { get; set; } = true;

        /// <summary>Theostone alerts in their own draggable window (default) or over the meter (v0.2.19).</summary>
        public TheostonePlace TheostonePlace { get; set; } = TheostonePlace.Screen;

        /// <summary>Where the alert window sits (dragged while Settings is open); null = default spot.</summary>
        public double? TheostoneLeft { get; set; }
        public double? TheostoneTop { get; set; }

        /// <summary>Alert size: 0.85, 1, 1.25 or 1.5.</summary>
        public double TheostoneScale { get; set; } = 1.0;

        /// <summary>How long a plain proc alert stays, seconds. Debuffs on you stay until they end.</summary>
        public double TheostoneSeconds { get; set; } = 3.5;

        // ---- arena: enemy Defiance alert (v0.2.27) ----

        /// <summary>The enemy's Defiance in its own draggable box during an arena match, Theostone style.</summary>
        public bool DefianceAlert { get; set; } = true;

        /// <summary>Where the Defiance box sits (dragged while Settings is open); null = default spot.</summary>
        public double? DefianceLeft { get; set; }
        public double? DefianceTop { get; set; }

        /// <summary>Defiance box size: 0.85, 1, 1.25 or 1.5.</summary>
        public double DefianceScale { get; set; } = 1.0;

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

        /// <summary>
        /// The server you fight in PvP, as typed ("Triniel EU"); turns into shugo.gg profile links for players whose
        /// server id Hawkeye does not know yet (see ShugoLinks).
        /// </summary>
        public string PvpEnemyServer { get; set; } = "";

        /// <summary>Download new releases from GitHub and install them when Hawkeye closes (or on click).</summary>
        public bool AutoUpdate { get; set; } = true;

        // ---- streamer mode (an OBS Browser Source page served by Hawkeye on this PC) ----
        public bool StreamerMode { get; set; } = false;
        /// <summary>http://localhost:{StreamerPort}/ (file only; change it if 7777 is taken).</summary>
        public int StreamerPort { get; set; } = 7777;
        public bool StreamShowMeter { get; set; } = true;
        public bool StreamShowIcons { get; set; } = true;
        /// <summary>Mirror the combat analysis window on stream while it is open.</summary>
        public bool StreamShowDetails { get; set; } = true;

        // ---- engine behaviour (no UI; edit the file if you ever need to) ----
        /// <summary>Only open a fight for bosses / training dummies, not trash mobs.</summary>
        public bool BossOnlyCapture { get; set; } = true;
        /// <summary>Count pet and summon damage as the owner's.</summary>
        public bool GroupSummonDamage { get; set; } = true;
        /// <summary>Save raw game traffic to the Packets folder, for testing the decoder offline.</summary>
        public bool RecordPackets { get; set; } = false;
    }
}
