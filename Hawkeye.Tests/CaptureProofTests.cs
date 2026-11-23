using Hawkeye.Engine.PacketCapture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// The capture proves a locked port is the game by reading a game keep-alive on it (v0.2.24). On 2026-11-23 a web
/// connection (port 443) won the lock after a character switch and the meter stayed blank on a dummy.
/// </summary>
public sealed class CaptureProofTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s);

    [Fact]
    public void GameBytes_GiveKeepAlives()
    {
        var buffer = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        // Real server bytes from a recording: keep-alives (0E 00 36 ...) with a damage message in between.
        buffer.AddData("Client:1", Hex("0E003667C3A9FEA1010000" + "0E00369AC3A9FEA1010000"
            + "190238E04400CF8E15012600E0444066A343C45A0100" + "0E0036CCC3A9FEA1010000"), 0);
        Assert.Equal(2, buffer.KeepAlives);   // the first is where it synced: proves nothing on its own
    }

    [Fact]
    public void AWholeKeepAliveInWebBytes_StillGivesNone()
    {
        var buffer = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var rnd = new Random(11);
        for (int i = 0; i < 200; i++)
        {
            var chunk = new byte[1400];
            rnd.NextBytes(chunk);
            if (i % 10 == 0) Hex("0E003667C3A9FEA1010000").CopyTo(chunk, 300); // a keep-alive's shape, alone in noise
            buffer.AddData("Client:3", chunk, 0);
        }
        Assert.Equal(0, buffer.KeepAlives);
    }

    [Fact]
    public void WebBytes_WithTheDetectionBytesByChance_GiveNone()
    {
        var buffer = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var rnd = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var chunk = new byte[1400];
            rnd.NextBytes(chunk);
            if (i % 20 == 0) { chunk[100] = 0x0E; chunk[101] = 0x00; chunk[102] = 0x36; } // the 3 bytes, as on port 443
            buffer.AddData("Client:2", chunk, 0);
        }
        Assert.Equal(0, buffer.KeepAlives);
    }
}
