using System.Text.Json;

namespace Hawkeye.Engine.Services.Updates
{
    /// <summary>
    /// The latest GitHub release as the updater needs it: its version and download links.
    /// Asset names are fixed by the release recipe (hawkeye-build-and-run): Hawkeye-&lt;ver&gt;-win-x64.zip (.NET
    /// included) and Hawkeye-&lt;ver&gt;-small.zip (needs the .NET runtime). Never rename them.
    /// </summary>
    public sealed record ReleaseInfo(Version Version, string Tag, IReadOnlyDictionary<string, string> Assets)
    {
        public const string LatestUrl = "https://api.github.com/repos/simbariya/hawkeye/releases/latest";

        /// <summary>"v0.2.7" or "0.2.7" (anything after '+' or '-' ignored); null if it is not a version.</summary>
        public static Version? ParseVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim().TrimStart('v', 'V');
            int cut = t.IndexOfAny(['+', '-', ' ']);
            if (cut >= 0) t = t[..cut];
            return Version.TryParse(t, out var v) ? Normalise(v) : null;
        }

        /// <summary>The zip that matches this copy: the full one for a self-contained build, else the small one.</summary>
        public static string AssetName(Version version, bool selfContained) =>
            $"Hawkeye-{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}-{(selfContained ? "win-x64" : "small")}.zip";

        /// <summary>Reads GitHub's "latest release" JSON; null if it is not a usable release.</summary>
        public static ReleaseInfo? FromGitHubJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
                if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
                var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                var version = ParseVersion(tag);
                if (version is null) return null;
                var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("assets", out var list))
                    foreach (var a in list.EnumerateArray())
                    {
                        var name = a.GetProperty("name").GetString();
                        var url = a.GetProperty("browser_download_url").GetString();
                        if (name is not null && url is not null) assets[name] = url;
                    }
                return new ReleaseInfo(version, tag, assets);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True when this release is newer than <paramref name="current"/> and has the zip this copy needs.</summary>
        public bool IsUpdateFor(Version current, bool selfContained) =>
            Version > Normalise(current) && Assets.ContainsKey(AssetName(Version, selfContained));

        private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
    }
}
