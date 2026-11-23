using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hawkeye.App.Services.Windowing;
using Hawkeye.App.ViewModels;
using Hawkeye.Engine.Services.Theostone;
using Hawkeye.Engine.Settings;

namespace Hawkeye.App.Views
{
    /// <summary>
    /// Theostone alerts in their own small always-on-top window (owner, 2026-11-22: "make it draggable as I want to
    /// drag it out to another portion of the screen"). Click-through and invisible when nothing is showing. While
    /// Settings is open it shows a dashed box with two sample alerts and can be dragged; the spot is saved.
    /// Plain WPF like the tracker icons. Streamer mode draws the same alerts at the same spot.
    ///
    /// Tiles side by side (owner, 2026-11-23: "instead of line message, a box that takes less space but taller, like a
    /// button that appears"): YOURS (cyan top edge) or ON YOU (red), the effect in a word ("Silence", "Damage 2%"),
    /// then the damage, or for a debuff its seconds left and a bar running down. A debuff stays until it ends or the
    /// game removes it (a potion).
    /// </summary>
    public sealed class TheostoneAlertWindow : Window
    {
        public const double TileWidth = 92;
        public const double TileHeight = 66;
        public const double TileGap = 3;
        private const double Pad = 6;
        private const double LabelRoom = 14;   // above the rows, for the "drag to move" label in edit mode

        private static readonly Color Cyan = Color.FromRgb(0x8C, 0xCF, 0xDF);
        private static readonly Color Red = Color.FromRgb(0xFF, 0x4D, 0x4D);
        private static readonly Brush CyanBrush = Frozen(new SolidColorBrush(Cyan));
        private static readonly Brush RedBrush = Frozen(new SolidColorBrush(Red));
        private static readonly Brush TagRed = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        private static readonly Brush YoursBack = Frozen(new SolidColorBrush(Color.FromRgb(14, 16, 20)));
        private static readonly Brush OnYouBack = Frozen(new SolidColorBrush(Color.FromRgb(38, 12, 14)));
        private static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEC)));
        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xE6, 0xE8, 0xEC)));
        private static readonly FontFamily Font = new("Segoe UI");

        // Grade colours for the word (owner, 2026-11-23: "of a colour of rarity, green/blue/gold"). Also in meter.css.
        public static readonly string[] GradeHex = ["#5FD068", "#4FA8FF", "#F2C14E"];
        private static readonly Brush[] GradeBrushes = GradeHex.Select(h => Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)))).ToArray();
        private static readonly Brush FlagBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xA9, 0x4D)));

        private static Brush WordBrush(TheostoneProc p) => p.Grade is >= 0 and <= 2 ? GradeBrushes[p.Grade] : Text;

        private readonly ISettingsService settings;
        private readonly TheostoneAlerts alerts;
        private readonly StackPanel rows;
        private readonly Rectangle editOutline;
        private readonly TextBlock editLabel;
        private readonly DispatcherTimer timer;
        private bool isEditMode;
        private string lastKey = "";

        /// <summary>What is on screen now, for streamer mode (empty when nothing shows).</summary>
        public IReadOnlyList<TheostoneProc> Shown { get; private set; } = [];
        public double ContentLeft => Left + Pad;
        public double ContentTop => Top + Pad + LabelRoom;
        public double Scale => Math.Clamp(settings.Current.TheostoneScale, 0.6, 2);
        public TheostoneAlerts Alerts => alerts;

        public TheostoneAlertWindow(ISettingsService settings, TheostoneAlerts alerts)
        {
            this.settings = settings;
            this.alerts = alerts;

            Title = "Hawkeye - Theostone alerts";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;

            rows = new StackPanel { Orientation = Orientation.Horizontal };
            editOutline = new Rectangle
            {
                Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 },
                Margin = new Thickness(-3), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            editLabel = new TextBlock
            {
                Text = "THEOSTONE ALERTS  (drag to move)", FontFamily = Font, FontSize = 9, Foreground = Brushes.White,
                Margin = new Thickness(0, -16, 0, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            var root = new Grid
            {
                Margin = new Thickness(Pad, Pad + LabelRoom, Pad, Pad),
                Background = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0))),   // grabbable in edit mode
            };
            root.Children.Add(rows);
            root.Children.Add(editOutline);
            root.Children.Add(editLabel);
            Content = root;

            RestorePosition();
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            SourceInitialized += (_, _) => ClickThrough.Set(this, !isEditMode);

            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => Refresh();
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }

        /// <summary>Edit mode = Settings is open: sample alerts, dashed box, draggable.</summary>
        public void SetEditMode(bool editMode)
        {
            isEditMode = editMode;
            editOutline.Visibility = editLabel.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
            Cursor = editMode ? Cursors.SizeAll : null;
            ClickThrough.Set(this, !editMode);
            lastKey = "";
            if (editMode) BringToFront();
            Refresh();
        }

        public void BringToFront()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        /// <summary>Two made-up alerts to place the window by.</summary>
        public static IReadOnlyList<TheostoneProc> Samples(DateTime now) =>
        [
            new(now, true, "Traufnir's Courage", 10_400, "", 1, Label: "Damage 1%", Grade: 1, Crit: true),
            new(now, false, "Khrudgelmir's Silence", 0, "Bravo", 0, now.AddSeconds(3.4), 1, TimeSpan.FromSeconds(4), "Silence", Grade: 1),
        ];

        private void Refresh()
        {
            var now = DateTime.Now;
            bool onScreen = settings.Current.TheostonePlace == TheostonePlace.Screen;
            var live = alerts.Showing;
            IReadOnlyList<TheostoneProc> list = !onScreen ? [] : live.Count > 0 ? live : isEditMode ? Samples(now) : [];
            Shown = onScreen ? live : [];   // the samples are only for placing it, not for the stream

            double scale = Scale;
            var key = string.Join("|", list.Select(p => $"{p.Name}{p.Yours}{p.Hits}{p.Damage}{p.IsDebuff}{p.From}{p.Flags}")) + scale;
            if (key != lastKey)
            {
                lastKey = key;
                rows.Children.Clear();
                foreach (var p in list) rows.Children.Add(BuildRow(p, scale));
            }
            // Live parts: countdown, bar, fade of plain alerts in their last half second.
            for (int i = 0; i < rows.Children.Count && i < list.Count; i++)
            {
                var p = list[i];
                var row = (Border)rows.Children[i];
                var parts = (RowParts)row.Tag;
                if (p.Until is { } until)
                {
                    var left = until - now;
                    if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                    parts.Main.Text = MainText(p, left);
                    double frac = p.Duration.TotalMilliseconds > 0 ? Math.Clamp(left.TotalMilliseconds / p.Duration.TotalMilliseconds, 0, 1) : 0;
                    parts.Bar.Width = Math.Max(0, TileWidth * scale * frac);
                    row.Opacity = 1;
                }
                else
                {
                    double age = (now - p.At).TotalSeconds, life = alerts.ShowFor.TotalSeconds;
                    row.Opacity = live.Count == 0 ? 1 : Math.Clamp((life - age) / 0.5, 0, 1);
                }
            }
        }

        private sealed record RowParts(TextBlock Main, Rectangle Bar);

        /// <summary>Top line of a tile: whose it is.</summary>
        public static string TagText(TheostoneProc p) => p.Yours ? "YOU" : "ON YOU";

        /// <summary>The big line: seconds left on a debuff, else the damage (empty when it did none).</summary>
        public static string MainText(TheostoneProc p, TimeSpan left) =>
            p.IsDebuff ? left.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : p.Damage > 0 ? MeterViewModel.Compact(p.Damage, shortThousands: true) : "";

        /// <summary>The small line: a debuff's damage so far, who it came from, or how many hits.</summary>
        public static string SubText(TheostoneProc p)
        {
            if (p.IsDebuff && p.Damage > 0) return MeterViewModel.Compact(p.Damage, shortThousands: true);
            if (!p.Yours && p.From.Length > 0) return p.From;
            return p.Hits > 1 ? $"x{p.Hits}" : "";
        }

        private static Border BuildRow(TheostoneProc p, double scale)
        {
            TextBlock Line(string text, double size, Brush brush, FontWeight weight) => new()
            {
                Text = text, FontFamily = Font, FontSize = size * scale, Foreground = brush, FontWeight = weight,
                TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            };
            var tag = Line(TagText(p), 8.5, p.Yours ? CyanBrush : TagRed, FontWeights.SemiBold);
            var word = Line(p.Word, 13, WordBrush(p), FontWeights.SemiBold);
            var main = Line(MainText(p, p.Until is { } u ? u - DateTime.Now : TimeSpan.Zero), 16, Text, FontWeights.Bold);
            main.Visibility = Visibility.Visible;   // keeps its room so a countdown does not jump
            // Small line: how it hit (CRIT / DOUBLE, orange), then the damage so far, who, or xN.
            string subText = SubText(p);
            var sub = Line(p.Flags.Length > 0 || subText.Length > 0 ? " " : "", 9, Dim, FontWeights.Normal);
            sub.Inlines.Clear();
            if (p.Flags.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(p.Flags) { Foreground = FlagBrush, FontWeight = FontWeights.Bold });
            if (p.Flags.Length > 0 && subText.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  "));
            if (subText.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(subText));
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(tag); stack.Children.Add(word); stack.Children.Add(main); stack.Children.Add(sub);

            var bar = new Rectangle
            {
                Height = Math.Max(2, 3 * scale), Fill = p.Yours ? CyanBrush : RedBrush, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, Visibility = p.IsDebuff ? Visibility.Visible : Visibility.Collapsed,
                Opacity = 0.85,
            };
            var body = new Grid();
            body.Children.Add(new Border { Child = stack, Padding = new Thickness(4 * scale, 3 * scale, 4 * scale, 4 * scale) });
            body.Children.Add(bar);
            return new Border
            {
                Width = TileWidth * scale,
                Height = TileHeight * scale,
                Background = p.Yours ? YoursBack : OnYouBack,
                BorderBrush = p.Yours ? CyanBrush : RedBrush,
                BorderThickness = new Thickness(0, 2, 0, 0),
                Margin = new Thickness(0, 0, TileGap, 0),
                Child = body,
                Tag = new RowParts(main, bar),
            };
        }

        private void RestorePosition()
        {
            var s = settings.Current;
            if (s.TheostoneLeft is double left && s.TheostoneTop is double top
                && left + 50 > SystemParameters.VirtualScreenLeft && top + 20 > SystemParameters.VirtualScreenTop
                && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                Left = left;
                Top = top;
                return;
            }
            // Not placed yet: a little above the screen centre, where your eyes are anyway.
            Left = SystemParameters.PrimaryScreenWidth / 2 - TileWidth - Pad;
            Top = SystemParameters.PrimaryScreenHeight * 0.28;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;
            OverlayManager.TryDragMove(this);
            double left = Left, top = Top;
            settings.Update(s => { s.TheostoneLeft = left; s.TheostoneTop = top; });
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
