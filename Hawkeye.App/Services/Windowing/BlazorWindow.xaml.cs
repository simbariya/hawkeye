using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace Hawkeye.App.Services.Windowing
{
    /// <summary>A frameless, always-on-top window that shows one Razor page (Settings, History, Extras, analysis).</summary>
    public partial class BlazorWindow : System.Windows.Window
    {
        /// <summary>
        /// A PNG of what the window shows right now (streamer mode puts it on stream). Null if the page is not
        /// ready yet or the window is closing. UI thread only.
        /// </summary>
        public async Task<byte[]?> CaptureAsync()
        {
            try
            {
                var core = BlazorWebView.WebView?.CoreWebView2;
                if (core is null || !IsVisible) return null;
                using var ms = new System.IO.MemoryStream();
                await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ms);
                return ms.ToArray();
            }
            catch
            {
                return null; // disposed mid-capture
            }
        }

        public BlazorWindow(IServiceProvider services, Type componentType)
        {
            InitializeComponent();
            BlazorWebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = componentType });
            BlazorWebView.Services = services;
            BlazorWebView.BlazorWebViewInitialized += (_, e) =>
                e.WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        }
    }
}
