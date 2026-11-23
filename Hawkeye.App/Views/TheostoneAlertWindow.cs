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
    /// Alerts side by side in the "Scope" style (v0.2.23, chosen by the owner from four mock-ups; tiles before that,
    /// owner 2026-11-23: "a box that takes less space but taller, like a button that appears"): YOURS (cyan top edge) or ON YOU (red), the effect in a word ("Silence", "Damage 2%"),
    /// then the damage, or for a debuff its seconds left and a bar running down. A debuff stays until it ends or the
    /// game removes it (a potion).
    /// </summary>
    public sealed class TheostoneAlertWindow : Window
    {
        // "Scope" style (owner chose it 2026-11-23 from four mock-ups): no box, the Hawkeye eye in a ring that runs down
        // with a debuff, the text trailing off to the right over a fading shade.
        public const double ItemWidth = 178;
        public const double RingSize = 44;
        public const double ItemGap = 4;
        private const double Pad = 6;
        private const double LabelRoom = 14;   // above the rows, for the "drag to move" label in edit mode

        private static readonly Color Cyan = Color.FromRgb(0x8C, 0xCF, 0xDF);
        private static readonly Color Red = Color.FromRgb(0xFF, 0x4D, 0x4D);
        private static readonly Brush CyanBrush = Frozen(new SolidColorBrush(Cyan));
        private static readonly Brush RedBrush = Frozen(new SolidColorBrush(Red));
        private static readonly Brush TagRed = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        private static readonly Brush FaintRed = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x4D, 0x4D)));
        private static readonly Brush Shade = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0xE6, 10, 12, 14), 0), new(Color.FromArgb(0xBF, 10, 12, 14), 0.6), new(Color.FromArgb(0, 10, 12, 14), 1),
        }, new Point(0, 0), new Point(1, 0)));
        private static readonly ImageSource? Eye = LoadEye();

        /// <summary>The Hawkeye eye, from the files embedded in the exe (the stream page serves the same one).</summary>
        private static ImageSource? LoadEye()
        {
            try
            {
                using var stream = typeof(TheostoneAlertWindow).Assembly.GetManifestResourceStream("hawkeye-web/images\\hawkeye-emblem.png");
                if (stream is null) return null;
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit();
                img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                img.StreamSource = stream;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
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
                    parts.Arc.Data = Arc(frac, RingSize * scale);
                    row.Opacity = 1;
                }
                else
                {
                    double age = (now - p.At).TotalSeconds, life = alerts.ShowFor.TotalSeconds;
                    row.Opacity = live.Count == 0 ? 1 : Math.Clamp((life - age) / 0.5, 0, 1);
                }
            }
        }

        private sealed record RowParts(System.Windows.Documents.Run Main, Path Arc);

        /// <summary>Top line of a tile: whose it is.</summary>
        /// <summary>Top line: YOU, or who hit you (owner, 2026-11-23: "instead of ON YOU make the person's name in there");
        /// ON YOU only when the game has not told us their name.</summary>
        public static string TagText(TheostoneProc p) => p.Yours ? "YOU" : p.From.Length > 0 ? p.From : "ON YOU";

        /// <summary>The big line: seconds left on a debuff, else the damage (empty when it did none).</summary>
        public static string MainText(TheostoneProc p, TimeSpan left) =>
            p.IsDebuff ? left.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : p.Damage > 0 ? MeterViewModel.Compact(p.Damage, shortThousands: true) : "";

        /// <summary>The small line (after any CRIT / DOUBLE tag): a debuff's damage so far, or how many hits.</summary>
        public static string SubText(TheostoneProc p)
        {
            if (p.IsDebuff && p.Damage > 0) return MeterViewModel.Compact(p.Damage, shortThousands: true);
            return p.Hits > 1 ? $"x{p.Hits}" : "";
        }

        /// <summary>The ring's arc: <paramref name="fraction"/> of a circle, clockwise from the top.</summary>
        private static Geometry Arc(double fraction, double size)
        {
            double r = size * 20 / 44, c = size / 2;   // radius 20 in a 44 ring at size M
            if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
            if (fraction <= 0.001) return Geometry.Empty;
            double a = fraction * 2 * Math.PI;
            var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        private static Border BuildRow(TheostoneProc p, double scale)
        {
            TextBlock Line(string text, double size, Brush brush, FontWeight weight) => new()
            {
                Text = text, FontFamily = Font, FontSize = size * scale, Foreground = brush, FontWeight = weight,
                TextTrimming = TextTrimming.CharacterEllipsis, Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            };

            // The ring: yours cyan, on you red; a debuff's ring runs down over a faint full one.
            double ring = RingSize * scale;
            var colour = p.Yours ? CyanBrush : RedBrush;
            var ringGrid = new Grid { Width = ring, Height = ring, VerticalAlignment = VerticalAlignment.Center };
            if (p.IsDebuff)
                ringGrid.Children.Add(new Ellipse { Stroke = FaintRed, StrokeThickness = 3 * scale, Margin = new Thickness(0.5 * scale) });
            double left = p.Until is { } u ? Math.Clamp((u - DateTime.Now).TotalMilliseconds / Math.Max(1, p.Duration.TotalMilliseconds), 0, 1) : 1;
            var arc = new Path { Stroke = colour, StrokeThickness = (p.Yours ? 2 : 3) * scale, Data = Arc(left, ring), Opacity = p.Yours ? 0.9 : 1 };
            ringGrid.Children.Add(arc);
            if (Eye is not null)
                ringGrid.Children.Add(new Image { Source = Eye, Margin = new Thickness(7 * scale), Stretch = Stretch.Uniform });

            // The text: who, what (in its grade colour), then the number with CRIT / DOUBLE and the small extra.
            var who = Line(TagText(p), 9, p.Yours ? CyanBrush : TagRed, FontWeights.SemiBold);
            var word = Line(p.Word, 14, WordBrush(p), FontWeights.SemiBold);
            var number = new TextBlock { FontFamily = Font, TextTrimming = TextTrimming.CharacterEllipsis };
            var main = new System.Windows.Documents.Run(MainText(p, p.Until is { } u2 ? u2 - DateTime.Now : TimeSpan.Zero))
                { FontSize = 16 * scale, FontWeight = FontWeights.Bold, Foreground = Text };
            number.Inlines.Add(main);
            if (p.Flags.Length > 0)
                number.Inlines.Add(new System.Windows.Documents.Run("  " + p.Flags) { FontSize = 9 * scale, FontWeight = FontWeights.Bold, Foreground = FlagBrush, BaselineAlignment = BaselineAlignment.Center });
            if (SubText(p) is { Length: > 0 } extra)
                number.Inlines.Add(new System.Windows.Documents.Run("  " + extra) { FontSize = 10 * scale, Foreground = Dim, BaselineAlignment = BaselineAlignment.Center });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8 * scale, 0, 0, 0) };
            text.Children.Add(who); text.Children.Add(word); text.Children.Add(number);

            var line = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(ringGrid, Dock.Left);
            line.Children.Add(ringGrid);
            line.Children.Add(text);
            return new Border
            {
                Width = ItemWidth * scale,
                Background = Shade,
                Padding = new Thickness(6 * scale, 5 * scale, 14 * scale, 5 * scale),
                Margin = new Thickness(0, 0, ItemGap * scale, 0),
                Child = line,
                Tag = new RowParts(main, arc),
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
            Left = SystemParameters.PrimaryScreenWidth / 2 - ItemWidth - Pad;
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
