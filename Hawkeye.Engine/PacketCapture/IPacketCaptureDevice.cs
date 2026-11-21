namespace Hawkeye.Engine.PacketCapture;

public interface IPacketCaptureDevice : IDisposable
{
    bool IsCapturing { get; }
    string? DeviceName { get; }
    void StartCapture();
    void StopCapture();
    void DiscoverAdapters();
}