using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace Hawkeye.App.Services.Windowing
{
    /// <summary>A frameless, always-on-top window that shows one Razor page (used for Settings).</summary>
    public partial class BlazorWindow : System.Windows.Window
    {
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
