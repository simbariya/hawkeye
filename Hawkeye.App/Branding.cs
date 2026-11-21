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
        /// Ko-fi (or any support) page. Empty = the Support button shows as "coming soon" and does nothing.
        /// Put the full link here (e.g. https://ko-fi.com/yourname) to switch it on.
        /// </summary>
        public const string SupportUrl = "";
    }
}
