using System.Windows;

namespace Hawkeye.App.Services.Windowing
{
    /// <summary>
    /// A window streamer mode can put on stream: a picture of its web content and where that content sits on screen.
    /// Hawkeye's pages (<see cref="BlazorWindow"/>) and the shugo.gg profile popup (<see cref="ProfileWindow"/>).
    /// </summary>
    public interface ICapturableWindow
    {
        bool IsVisible { get; }

        /// <summary>Screen rectangle the picture covers (the profile popup's picture leaves out its header bar).</summary>
        Rect CaptureBounds { get; }

        /// <summary>A PNG of the web content now, or null (not ready, closing, or too slow). UI thread only.</summary>
        Task<byte[]?> CaptureAsync();
    }
}
