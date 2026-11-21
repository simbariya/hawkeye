using System.Windows;
using System.Windows.Threading;
using Hawkeye.App.Pages;
using Hawkeye.Engine.Settings;
using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace Hawkeye.App.Views
{
    /// <summary>
    /// The meter window: frameless, always on top, draggable (from the page) and resizable from invisible
    /// right, bottom and corner edges on the page (OverlayManager.TryResize). No visible grip.
    /// Opacity, position and size come from settings and are saved back a moment after you stop moving it.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ISettingsService settings;
        private readonly DispatcherTimer saveBoundsTimer;

        public MainWindow(ISettingsService settings, IServiceProvider services)
        {
            InitializeComponent();
            this.settings = settings;

            WebView.Services = services;
            WebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(MeterPage) });
            WebView.BlazorWebViewInitialized += (_, e) => e.WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            RestoreBounds();
            Root.Opacity = settings.Current.Opacity;
            settings.Changed += (_, _) => Dispatcher.BeginInvoke(() => Root.Opacity = settings.Current.Opacity);

            saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            saveBoundsTimer.Tick += (_, _) => { saveBoundsTimer.Stop(); SaveBounds(); };
            LocationChanged += (_, _) => RestartSaveTimer();
            SizeChanged += (_, _) => RestartSaveTimer();
        }

        private void RestartSaveTimer()
        {
            if (!IsLoaded) return;
            saveBoundsTimer.Stop();
            saveBoundsTimer.Start();
        }

        private void RestoreBounds()
        {
            var s = settings.Current;
            if (s.WindowWidth is double w) Width = Math.Max(MinWidth, w);
            if (s.WindowHeight is double h) Height = Math.Max(MinHeight, h);
            if (s.WindowLeft is not double left || s.WindowTop is not double top) return;

            // Keep it on a screen that still exists.
            var vLeft = SystemParameters.VirtualScreenLeft;
            var vTop = SystemParameters.VirtualScreenTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(left, vLeft, vLeft + SystemParameters.VirtualScreenWidth - Width);
            Top = Math.Clamp(top, vTop, vTop + SystemParameters.VirtualScreenHeight - Height);
        }

        private void SaveBounds()
        {
            if (WindowState != WindowState.Normal) return;
            double l = Left, t = Top, w = Width, h = Height;
            settings.Update(s =>
            {
                s.WindowLeft = l;
                s.WindowTop = t;
                s.WindowWidth = w;
                s.WindowHeight = h;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            saveBoundsTimer.Stop();
            SaveBounds();
            base.OnClosed(e);
        }
    }
}
