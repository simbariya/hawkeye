using System.Diagnostics;
using Hawkeye.Engine.Services.Updates;

namespace Hawkeye.Tests;

/// <summary>Runs the real install script against temporary folders (paths with spaces, like the owner's).</summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"hawkeye update test {Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private static int RunScript(string scriptPath)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(p.WaitForExit(60_000), "script did not finish");
        return p.ExitCode;
    }

    [Fact]
    public void WaitsForTheAppToExit_ThenCopiesEverythingOver()
    {
        var newFiles = Path.Combine(root, "download", "Hawkeye");
        var install = Path.Combine(root, "Aion 2 DPS meter", "Hawkeye");
        Directory.CreateDirectory(Path.Combine(newFiles, "wwwroot", "css"));
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(newFiles, "Hawkeye.exe"), "new exe");
        File.WriteAllText(Path.Combine(newFiles, "wwwroot", "css", "meter.css"), "new css");
        File.WriteAllText(Path.Combine(install, "Hawkeye.exe"), "old exe");
        File.WriteAllText(Path.Combine(install, "keep.txt"), "user file");

        // Stand-in for the running app: a process that lives ~3 s. The script must wait for it.
        using var app = Process.Start(new ProcessStartInfo("ping", "-n 4 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        var script = Path.Combine(root, "install-update.cmd");
        File.WriteAllLines(script, UpdateInstaller.Script(app.Id, newFiles, install, restart: false));

        var started = DateTime.UtcNow;
        RunScript(script);

        Assert.True(app.HasExited, "copied before the app had exited");
        Assert.True(DateTime.UtcNow - started > TimeSpan.FromSeconds(1.5));
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(install, "Hawkeye.exe")));
        Assert.Equal("new css", File.ReadAllText(Path.Combine(install, "wwwroot", "css", "meter.css")));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(install, "keep.txt")));
        Assert.False(File.Exists(script), "script should delete itself");
    }
}
