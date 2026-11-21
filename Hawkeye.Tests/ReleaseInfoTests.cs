using Hawkeye.Engine.Services.Updates;

namespace Hawkeye.Tests;

/// <summary>The updater's decisions: which release counts as newer and which zip to fetch.</summary>
public sealed class ReleaseInfoTests
{
    private const string Json = """
        {"tag_name":"v0.2.7","draft":false,"prerelease":false,"assets":[
          {"name":"Hawkeye-0.2.7-win-x64.zip","browser_download_url":"https://example/full.zip"},
          {"name":"Hawkeye-0.2.7-small.zip","browser_download_url":"https://example/small.zip"}]}
        """;

    [Fact]
    public void NewerRelease_IsAnUpdate_WithTheRightZip()
    {
        var r = ReleaseInfo.FromGitHubJson(Json)!;
        Assert.Equal(new Version(0, 2, 7), r.Version);
        Assert.True(r.IsUpdateFor(new Version(0, 2, 6), selfContained: true));
        Assert.Equal("https://example/full.zip", r.Assets[ReleaseInfo.AssetName(r.Version, selfContained: true)]);
        Assert.Equal("https://example/small.zip", r.Assets[ReleaseInfo.AssetName(r.Version, selfContained: false)]);
    }

    [Fact]
    public void SameOrOlder_IsNotAnUpdate()
    {
        var r = ReleaseInfo.FromGitHubJson(Json)!;
        Assert.False(r.IsUpdateFor(new Version(0, 2, 7), true));
        Assert.False(r.IsUpdateFor(new Version(0, 3, 0), true));
    }

    [Fact]
    public void ReleaseMissingYourZip_IsNotOffered()
    {
        var r = ReleaseInfo.FromGitHubJson(Json.Replace("Hawkeye-0.2.7-small.zip", "other.zip"))!;
        Assert.False(r.IsUpdateFor(new Version(0, 2, 6), selfContained: false));
    }

    [Theory]
    [InlineData("v0.2.6", 0, 2, 6)]
    [InlineData("0.2.6+ed23255", 0, 2, 6)]
    [InlineData("v1.0", 1, 0, 0)]
    public void Versions_AreRead(string text, int a, int b, int c) =>
        Assert.Equal(new Version(a, b, c), ReleaseInfo.ParseVersion(text));

    [Fact]
    public void DraftsAndJunk_AreIgnored()
    {
        Assert.Null(ReleaseInfo.FromGitHubJson(Json.Replace("\"draft\":false", "\"draft\":true")));
        Assert.Null(ReleaseInfo.FromGitHubJson("{not json"));
        Assert.Null(ReleaseInfo.ParseVersion("latest"));
    }
}
