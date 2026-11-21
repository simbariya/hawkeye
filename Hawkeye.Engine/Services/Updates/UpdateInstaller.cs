namespace Hawkeye.Engine.Services.Updates
{
    /// <summary>
    /// The batch file that installs a downloaded update: it waits until Hawkeye (by process id) has exited, copies
    /// the new files over the install folder with robocopy, optionally starts Hawkeye again, then deletes itself.
    /// Kept here, apart from the app, so a test can run it for real against temporary folders.
    /// </summary>
    public static class UpdateInstaller
    {
        public static string[] Script(int waitForPid, string newFilesFolder, string installFolder, bool restart)
        {
            var install = installFolder.TrimEnd('\\', '/');
            var source = newFilesFolder.TrimEnd('\\', '/');
            // Full System32 paths: a plain "find" can resolve to another program (Git's find.exe was found first
            // on PATH in testing, so the script copied without waiting).
            const string sys = @"%SystemRoot%\System32\";
            return
            [
                "@echo off",
                ":wait",
                $"{sys}tasklist.exe /FI \"PID eq {waitForPid}\" /NH 2>nul | {sys}find.exe \" {waitForPid} \" >nul",
                $"if not errorlevel 1 ({sys}PING.EXE -n 2 127.0.0.1 >nul & goto wait)",
                // Hawkeye's window runs on WebView2, whose helper processes outlive Hawkeye by a moment and hold
                // the WebView2 data folder next to the exe. Restarting while they still did left the new copy
                // without a window (0x8007139F, 0.2.8 to 0.2.9 on 2026-11-21, I14). Give them 3 seconds.
                $"{sys}PING.EXE -n 4 127.0.0.1 >nul",
                $"{sys}Robocopy.exe \"{source}\" \"{install}\" /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP >nul",
                restart ? $"start \"\" /D \"{install}\" \"{Path.Combine(install, "Hawkeye.exe")}\"" : "rem no restart",
                "del \"%~f0\"",
            ];
        }
    }
}
