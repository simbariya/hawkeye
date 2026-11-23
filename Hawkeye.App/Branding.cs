namespace Hawkeye.App
{
    /// <summary>
    /// Who made Hawkeye and where to find them. Shown at the bottom of the Settings window and in the README.
    /// </summary>
    public static class Branding
    {
        public const string Author = "Simba";

        /// <summary>"v0.1.4": from the csproj &lt;Version&gt;, so it can never drift from the build.</summary>
        public static readonly string Version = "v" + (
            (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Branding).Assembly)
                ?.InformationalVersion ?? "0.0.0").Split('+')[0]);
        public const string TwitchUrl = "https://twitch.tv/drake22x";
        public const string TwitchName = "twitch.tv/drake22x";

        /// <summary>
        /// The owner's Ko-fi page (given 2026-11-23, same as on salsaghost.com/donate). Empty = the Support button
        /// shows as "coming soon" and does nothing.
        /// </summary>
        public const string SupportUrl = "https://ko-fi.com/X3N527KYCV";
    }
}
