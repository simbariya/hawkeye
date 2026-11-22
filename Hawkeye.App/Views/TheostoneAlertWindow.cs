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
    /// Rows: THEOSTONE (your proc, cyan edge), HIT BY (someone's hit you, red edge). A debuff on you shows its time
    /// left and a bar running down, and stays until it ends or the game removes it (potion).
    /// </summary>
    public sealed class TheostoneAlertWindow : Window
    {
        public const double BaseWidth = 300;
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

            rows = new StackPanel();
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
            new(now, true, "Tassin's Command", 12_400, "", 2),
            new(now, false, "Khrudgelmir's Silence", 0, "Bravo", 0, now.AddSeconds(3.4), 1, TimeSpan.FromSeconds(4)),
        ];

        private void Refresh()
        {
            var now = DateTime.Now;
            bool onScreen = settings.Current.TheostonePlace == TheostonePlace.Screen;
            var live = alerts.Showing;
            IReadOnlyList<TheostoneProc> list = !onScreen ? [] : live.Count > 0 ? live : isEditMode ? Samples(now) : [];
            Shown = onScreen ? live : [];   // the samples are only for placing it, not for the stream

            double scale = Scale;
            var key = string.Join("|", list.Select(p => $"{p.Name}{p.Yours}{p.Hits}{p.Damage}{p.IsDebuff}")) + scale;
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
                    parts.Amount.Text = AmountText(p, left);
                    double frac = p.Duration.TotalMilliseconds > 0 ? Math.Clamp(left.TotalMilliseconds / p.Duration.TotalMilliseconds, 0, 1) : 0;
                    parts.Bar.Width = Math.Max(0, (BaseWidth * scale - 2) * frac);
                    row.Opacity = 1;
                }
                else
                {
                    double age = (now - p.At).TotalSeconds, life = alerts.ShowFor.TotalSeconds;
                    row.Opacity = live.Count == 0 ? 1 : Math.Clamp((life - age) / 0.5, 0, 1);
                }
            }
        }

        private sealed record RowParts(TextBlock Amount, Rectangle Bar);

        public static string TagText(TheostoneProc p) => p.Yours ? "THEOSTONE" : "HIT BY";

        public static string AmountText(TheostoneProc p, TimeSpan left)
        {
            var parts = new List<string>();
            if (p.Damage > 0) parts.Add(MeterViewModel.Compact(p.Damage, shortThousands: true) + (p.Hits > 1 ? $" x{p.Hits}" : ""));
            if (p.IsDebuff) parts.Add(left.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
            return string.Join("  ", parts);
        }

        private static Border BuildRow(TheostoneProc p, double scale)
        {
            double fs = 12 * scale;
            var tag = new TextBlock
            {
                Text = TagText(p), FontFamily = Font, FontSize = 9 * scale, Foreground = p.Yours ? CyanBrush : TagRed,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8 * scale, 0),
            };
            var name = new TextBlock
            {
                Text = MeterViewModel.ProcText(p), FontFamily = Font, FontSize = fs, Foreground = Text,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            var amount = new TextBlock
            {
                Text = AmountText(p, p.Until is { } u ? u - DateTime.Now : TimeSpan.Zero), FontFamily = Font, FontSize = fs,
                Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8 * scale, 0, 0, 0),
            };
            var bar = new Rectangle
            {
                Height = Math.Max(2, 2 * scale), Fill = RedBrush, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, Visibility = p.IsDebuff ? Visibility.Visible : Visibility.Collapsed,
                Opacity = 0.8,
            };
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(tag, 0); Grid.SetColumn(name, 1); Grid.SetColumn(amount, 2);
            line.Children.Add(tag); line.Children.Add(name); line.Children.Add(amount);

            var body = new Grid();
            body.Children.Add(new Border { Child = line, Padding = new Thickness(8 * scale, 4 * scale, 8 * scale, 4 * scale) });
            body.Children.Add(bar);
            return new Border
            {
                Width = BaseWidth * scale,
                Background = p.Yours ? YoursBack : OnYouBack,
                BorderBrush = p.Yours ? CyanBrush : RedBrush,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, 1),
                Child = body,
                Tag = new RowParts(amount, bar),
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
            Left = SystemParameters.PrimaryScreenWidth / 2 - BaseWidth / 2 - Pad;
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
