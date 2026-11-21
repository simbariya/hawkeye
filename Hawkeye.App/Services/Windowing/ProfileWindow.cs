using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hawkeye.App.Services.Windowing
{
    /// <summary>
    /// A player's shugo.gg profile inside the overlay (owner, 2026-11-21: "open in a popup ... with an option to open
    /// in browser ... so it is all part of the overlay"). A plain web view in a frameless, always-on-top, resizable
    /// window with a slim header: the player's name, Open in browser, close.
    ///
    /// It has its OWN browser data folder (%LocalAppData%\Hawkeye\ProfileBrowser): a web site's cookies stay apart
    /// from Hawkeye's pages, and its browser processes never share (or hold) the overlay's data folder.
    /// </summary>
    public sealed class ProfileWindow : Window
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hawkeye", "ProfileBrowser");

        private readonly WebView2 web;
        private readonly TextBlock title;
        private string url;

        public ProfileWindow(string url, string name)
        {
            this.url = url;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Topmost = true;
            ShowInTaskbar = false;
            Width = 980;
            Height = 720;
            Background = Brush("#17181D");
            BorderBrush = Brush("#3A3D48");
            BorderThickness = new Thickness(1);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });

            title = new TextBlock
            {
                Foreground = Brush("#E6E8EC"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var browserButton = HeaderButton("Open in browser", 118);
            browserButton.Click += (_, _) => OpenInBrowser();
            var closeButton = HeaderButton("✕", 34);
            closeButton.Click += (_, _) => Close();

            var header = new DockPanel { Height = 32, Background = Brush("#1F2027"), LastChildFill = true };
            DockPanel.SetDock(closeButton, Dock.Right);
            DockPanel.SetDock(browserButton, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(browserButton);
            header.Children.Add(title);
            header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) TryDrag(); };

            web = new WebView2
            {
                CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = DataFolder },
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x17, 0x18, 0x1D),
            };

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(web);
            Content = root;

            Show(url, name);
            Closed += (_, _) => web.Dispose();
        }

        /// <summary>Shows another player in this same window.</summary>
        public void Show(string newUrl, string name)
        {
            url = newUrl;
            title.Text = $"{name}   shugo.gg";
            Title = $"{name} - shugo.gg";
            try { web.Source = new Uri(newUrl); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[PVP] could not open profile {Url}", newUrl); }
        }

        private void OpenInBrowser()
        {
            var current = web.Source?.ToString() ?? url;
            try { Process.Start(new ProcessStartInfo(current) { UseShellExecute = true }); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[PVP] could not open {Url} in the browser", current); }
        }

        private void TryDrag()
        {
            try { DragMove(); } catch (InvalidOperationException) { /* mouse already released */ }
        }

        private static Button HeaderButton(string text, double width) => new()
        {
            Content = text,
            Width = width,
            Foreground = Brush("#C9CCD4"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            Focusable = false,
        };

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
    }
}
