using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hawkeye.App.Services.Windowing;
using Hawkeye.Core.Data;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Engine.Services.Arena;
using Hawkeye.Engine.Settings;

namespace Hawkeye.App.Views
{
    /// <summary>
    /// The enemy's Defiance during an arena match, in its own small always-on-top window you drag anywhere (owner,
    /// 2026-11-23: "defiance ready/cd I want to drag it out like we do with godstones and be in same style, just a
    /// defiance icon instead"). One tile per enemy in the Theostone "Scope" style: the Defiance icon in a ring, red
    /// and full while it is ready (it would break your stun), green and running down while it is on cooldown (stun
    /// now). Click-through; while Settings is open it shows a dashed box with two samples and can be dragged.
    /// </summary>
    public sealed class DefianceAlertWindow : Window
    {
        public const double ItemWidth = TheostoneAlertWindow.ItemWidth;
        private const double RingSize = TheostoneAlertWindow.RingSize;
        private const double ItemGap = TheostoneAlertWindow.ItemGap;
        private const double Pad = 6;
        private const double LabelRoom = 14;
        /// <summary>Any class's Defiance: they all share the one icon (ICON_CO_SKILL_002).</summary>
        private const int DefianceSkillId = 15240000;

        private static readonly Brush ReadyBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)));
        private static readonly Brush ReadyText = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        private static readonly Brush DownBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x5F, 0xD0, 0x68)));
        private static readonly Brush FaintDown = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x5F, 0xD0, 0x68)));
        private static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEC)));
        private static readonly Brush Shade = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0xE6, 10, 12, 14), 0), new(Color.FromArgb(0xBF, 10, 12, 14), 0.6), new(Color.FromArgb(0, 10, 12, 14), 1),
        }, new Point(0, 0), new Point(1, 0)));
        private static readonly FontFamily Font = new("Segoe UI");

        private readonly ISettingsService settings;
        private readonly ArenaTracker arena;
        private readonly StackPanel rows;
        private readonly Rectangle editOutline;
        private readonly TextBlock editLabel;
        private readonly DispatcherTimer timer;
        private ImageSource? icon;
        private bool isEditMode;
        private string lastKey = "";

        /// <summary>One tile: whose Defiance, and how long until it is back (zero = ready).</summary>
        public readonly record struct Tile(string Name, TimeSpan Left, TimeSpan Cooldown)
        {
            public bool Down => Left > TimeSpan.Zero;
            public string Main => Down ? $"{Math.Ceiling(Left.TotalSeconds):0}s" : "Ready";
            public double Fraction => Down && Cooldown > TimeSpan.Zero ? Math.Clamp(Left / Cooldown, 0, 1) : 1;
        }

        /// <summary>What is on screen now, for the stream page (empty when nothing shows).</summary>
        public IReadOnlyList<Tile> Shown { get; private set; } = [];
        public double ContentLeft => Left + Pad;
        public double ContentTop => Top + Pad + LabelRoom;
        public double Scale => Math.Clamp(settings.Current.DefianceScale, 0.6, 2);
        public static string IconUrl => GameDataProvider.Instance.GetSkillOrDefault(DefianceSkillId).Icon ?? "";

        public DefianceAlertWindow(ISettingsService settings, ArenaTracker arena)
        {
            this.settings = settings;
            this.arena = arena;

            Title = "Hawkeye - Defiance";
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
                Text = "ENEMY DEFIANCE  (drag to move)", FontFamily = Font, FontSize = 9, Foreground = Brushes.White,
                Margin = new Thickness(0, -16, 0, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            var root = new Grid
            {
                Margin = new Thickness(Pad, Pad + LabelRoom, Pad, Pad),
                Background = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0))),
            };
            root.Children.Add(rows);
            root.Children.Add(editOutline);
            root.Children.Add(editLabel);
            Content = root;

            RestorePosition();
            LoadIcon();
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            SourceInitialized += (_, _) => ClickThrough.Set(this, !isEditMode);

            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => Refresh();
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }

        private void LoadIcon()
        {
            var url = IconUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            var path = SkillIconCache.Instance.GetLocalPathOrStartDownload(url, onDownloaded: () => Dispatcher.BeginInvoke(() => { LoadIcon(); lastKey = ""; }));
            if (path is null) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                icon = bmp;
            }
            catch { /* broken cache file: tiles go without the icon */ }
        }

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

        /// <summary>The tiles to show now: each enemy in a running match.</summary>
        public static IReadOnlyList<Tile> Live(ArenaTracker arena, bool enabled) =>
            !enabled || arena.Match is not { Won: null } m ? []
            : m.Enemies.Select(e => new Tile(e.Name, arena.DefianceLeft(e), e.DefianceCooldown)).ToList();

        private static IReadOnlyList<Tile> Samples() =>
        [
            new("Rival", TimeSpan.Zero, TimeSpan.FromSeconds(60)),
            new("Bravo", TimeSpan.FromSeconds(38), TimeSpan.FromSeconds(51.3)),
        ];

        private void Refresh()
        {
            var live = Live(arena, settings.Current.DefianceAlert);
            var list = live.Count > 0 ? live : isEditMode ? Samples() : [];
            Shown = live;

            double scale = Scale;
            var key = string.Join("|", list.Select(t => t.Name + t.Down)) + scale + (icon is null);
            if (key != lastKey)
            {
                lastKey = key;
                rows.Children.Clear();
                foreach (var t in list) rows.Children.Add(BuildRow(t, scale));
            }
            for (int i = 0; i < rows.Children.Count && i < list.Count; i++)
            {
                var parts = (RowParts)((Border)rows.Children[i]).Tag;
                parts.Main.Text = list[i].Main;
                parts.Arc.Data = Arc(list[i].Fraction, RingSize * scale);
            }
        }

        private sealed record RowParts(TextBlock Main, Path Arc);

        private static Geometry Arc(double fraction, double size)
        {
            double r = size * 20 / 44, c = size / 2;
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

        private Border BuildRow(Tile t, double scale)
        {
            TextBlock Line(string text, double size, Brush brush, FontWeight weight) => new()
            {
                Text = text, FontFamily = Font, FontSize = size * scale, Foreground = brush, FontWeight = weight,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            double ring = RingSize * scale;
            var colour = t.Down ? DownBrush : ReadyBrush;
            var ringGrid = new Grid { Width = ring, Height = ring, VerticalAlignment = VerticalAlignment.Center };
            if (t.Down) ringGrid.Children.Add(new Ellipse { Stroke = FaintDown, StrokeThickness = 3 * scale, Margin = new Thickness(0.5 * scale) });
            var arc = new Path { Stroke = colour, StrokeThickness = 3 * scale, Data = Arc(t.Fraction, ring) };
            ringGrid.Children.Add(arc);
            if (icon is not null)
                ringGrid.Children.Add(new Image { Source = icon, Margin = new Thickness(7 * scale), Stretch = Stretch.Uniform, Opacity = t.Down ? 0.55 : 1 });

            var who = Line(t.Name, 9, ReadyText, FontWeights.SemiBold);
            var word = Line("Defiance", 14, Text, FontWeights.SemiBold);
            var main = Line(t.Main, 16, colour, FontWeights.Bold);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8 * scale, 0, 0, 0) };
            text.Children.Add(who); text.Children.Add(word); text.Children.Add(main);

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
            if (s.DefianceLeft is double left && s.DefianceTop is double top
                && left + 50 > SystemParameters.VirtualScreenLeft && top + 20 > SystemParameters.VirtualScreenTop
                && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                Left = left;
                Top = top;
                return;
            }
            // Not placed yet: just below where the Theostone alerts start.
            Left = SystemParameters.PrimaryScreenWidth / 2 - ItemWidth - Pad;
            Top = SystemParameters.PrimaryScreenHeight * 0.28 + 80;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;
            OverlayManager.TryDragMove(this);
            double left = Left, top = Top;
            settings.Update(s => { s.DefianceLeft = left; s.DefianceTop = top; });
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
