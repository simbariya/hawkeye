using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hawkeye.App.Services.Windowing;
using Hawkeye.Core.Data;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;

namespace Hawkeye.App.Views
{
    /// <summary>
    /// One tracked skill on screen: its own small, square, always-on-top window.
    ///
    /// Cooldown mode
    ///   ready       full-colour icon, thin light frame; a short orange flash when it comes off cooldown
    ///   cooling     dark clock sweep over the icon + countdown (yellow for the last 2 seconds)
    /// Buff mode
    ///   up          full-colour icon, green frame, countdown, thin bar along the bottom showing time left
    ///               (frame and text turn yellow for the last 3 seconds)
    ///   down        darkened icon with a pulsing red frame: time to reapply
    ///
    /// While Settings is open (edit mode) it shows a dashed outline and a CD/BUFF tag, and can be dragged;
    /// the spot is saved in settings.json. Otherwise it is click-through and never steals focus.
    /// Plain WPF (no web view) so ten icons cost next to nothing.
    /// </summary>
    public sealed class TrackerIconWindow : Window
    {
        private const double Pad = 6; // room around the icon for the glow
        private static readonly TimeSpan ReadyFlashLength = TimeSpan.FromMilliseconds(700);

        private static readonly Color Orange = Color.FromRgb(0xDE, 0x89, 0x3A);
        private static readonly Color Green = Color.FromRgb(0x58, 0xFF, 0x77);
        private static readonly Color Yellow = Color.FromRgb(0xFF, 0xD1, 0x66);
        private static readonly Color Red = Color.FromRgb(0xFF, 0x4D, 0x4D);

        private static readonly Brush ReadyFrame = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush CoolingFrame = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush GreenBrush = Frozen(new SolidColorBrush(Green));
        private static readonly Brush YellowBrush = Frozen(new SolidColorBrush(Yellow));
        private static readonly Brush RedBrush = Frozen(new SolidColorBrush(Red));
        private static readonly Brush SweepBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB4, 0, 0, 0)));
        private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)));

        private readonly int skillId;
        private readonly string skillName;
        private readonly int defaultSlot;
        private readonly ISettingsService settings;
        private readonly ITimedEventTracker cooldowns;
        private readonly ITimedEventTracker buffs;
        private readonly DispatcherTimer timer;

        private readonly Grid iconArea;
        private readonly Image image;
        private readonly TextBlock fallbackLetter;
        private readonly Path sweep;
        private readonly Rectangle dim;
        private readonly Rectangle buffBar;
        private readonly OutlinedText countdown;
        private readonly Border frame;
        private readonly Rectangle editOutline;
        private readonly Border modeTag;
        private readonly TextBlock modeTagText;
        private readonly DropShadowEffect glow;

        private double iconSize;
        private bool isEditMode;
        private bool wasActive;
        private DateTime flashUntilUtc = DateTime.MinValue;

        public TrackerIconWindow(int skillId, int defaultSlot, ISettingsService settings,
            ITimedEventTracker cooldowns, ITimedEventTracker buffs)
        {
            this.skillId = skillId;
            this.defaultSlot = defaultSlot;
            this.settings = settings;
            this.cooldowns = cooldowns;
            this.buffs = buffs;

            var skill = GameDataProvider.Instance.GetSkillOrDefault(skillId);
            skillName = skill.Name;

            Title = $"Hawkeye - {skillName}";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;

            image = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            fallbackLetter = new TextBlock
            {
                Text = skillName.Length > 0 ? skillName[0].ToString().ToUpperInvariant() : "?",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            sweep = new Path { Fill = SweepBrush, IsHitTestVisible = false };
            dim = new Rectangle { Fill = DimBrush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            buffBar = new Rectangle
            {
                Fill = GreenBrush,
                Height = 3,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };

            countdown = new OutlinedText
            {
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };

            iconArea = new Grid { Background = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x27))), ClipToBounds = true };
            iconArea.Children.Add(fallbackLetter);
            iconArea.Children.Add(image);
            iconArea.Children.Add(dim);
            iconArea.Children.Add(sweep);
            iconArea.Children.Add(buffBar);
            iconArea.Children.Add(countdown);

            glow = new DropShadowEffect { Color = Orange, BlurRadius = 12, ShadowDepth = 0, Opacity = 0 };
            frame = new Border { BorderThickness = new Thickness(2), IsHitTestVisible = false, Effect = glow };

            editOutline = new Rectangle
            {
                Stroke = Brushes.White,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Margin = new Thickness(-3),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };

            modeTagText = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, Margin = new Thickness(3, 0, 3, 0) };
            modeTag = new Border
            {
                Child = modeTagText,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };

            var root = new Grid
            {
                Margin = new Thickness(Pad),
                // Near-transparent fill so the whole square can be grabbed in edit mode.
                Background = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0))),
            };
            root.Children.Add(iconArea);
            root.Children.Add(frame);
            root.Children.Add(editOutline);
            root.Children.Add(modeTag);
            Content = root;

            ApplySize();
            LoadIcon(skill.Icon);
            RestorePosition();

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            settings.Changed += OnSettingsChanged;

            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => Refresh();
            timer.Start();

            Closed += (_, _) =>
            {
                timer.Stop();
                settings.Changed -= OnSettingsChanged;
            };

            Refresh();
        }

        private TrackMode Mode =>
            settings.Current.TrackedSkills.FirstOrDefault(t => t.SkillId == skillId)?.Mode ?? TrackMode.Cooldown;

        /// <summary>Edit mode = Settings is open: draggable, outlined, raised above everything.</summary>
        public void SetEditMode(bool editMode)
        {
            isEditMode = editMode;
            editOutline.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
            modeTag.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
            Cursor = editMode ? Cursors.SizeAll : null;
            ToolTip = editMode ? $"{skillName} (drag to move)" : null;
            ClickThrough.Set(this, !editMode);
            if (editMode) BringToFront();
        }

        /// <summary>Puts this icon on top of every other always-on-top window without taking focus.</summary>
        public void BringToFront()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void Refresh()
        {
            var mode = Mode;
            var timers = mode == TrackMode.Buff ? buffs : cooldowns;
            var state = timers.Get(unchecked((uint)skillId));
            bool active = state is not null && state.TimeLeft > TimeSpan.FromMilliseconds(50);
            var now = DateTime.UtcNow;

            modeTag.Background = mode == TrackMode.Buff ? GreenBrush : ReadyFrame;
            modeTagText.Text = mode == TrackMode.Buff ? "BUFF" : "CD";

            if (mode == TrackMode.Cooldown) RefreshCooldown(state, active, now);
            else RefreshBuff(state, active, now);

            wasActive = active;
        }

        private void RefreshCooldown(TimedItemState? state, bool onCooldown, DateTime now)
        {
            dim.Visibility = Visibility.Collapsed;
            buffBar.Visibility = Visibility.Collapsed;

            if (wasActive && !onCooldown) flashUntilUtc = now + ReadyFlashLength;

            if (onCooldown)
            {
                sweep.Data = BuildSweep(Fraction(state!), iconSize);
                sweep.Visibility = Visibility.Visible;
                ShowCountdown(state!.TimeLeft, state.TimeLeft <= TimeSpan.FromSeconds(2));
                frame.BorderBrush = CoolingFrame;
                glow.Opacity = 0;
            }
            else
            {
                sweep.Visibility = Visibility.Collapsed;
                countdown.Visibility = Visibility.Collapsed;
                frame.BorderBrush = ReadyFrame;
                glow.Color = Orange;
                var flashLeft = (flashUntilUtc - now).TotalMilliseconds;
                glow.Opacity = flashLeft > 0 ? flashLeft / ReadyFlashLength.TotalMilliseconds : 0;
            }
        }

        private void RefreshBuff(TimedItemState? state, bool up, DateTime now)
        {
            sweep.Visibility = Visibility.Collapsed;

            if (up)
            {
                bool ending = state!.TimeLeft <= TimeSpan.FromSeconds(3);
                dim.Visibility = Visibility.Collapsed;
                buffBar.Visibility = Visibility.Visible;
                buffBar.Width = iconSize * Fraction(state);
                buffBar.Fill = ending ? YellowBrush : GreenBrush;
                ShowCountdown(state.TimeLeft, ending);
                frame.BorderBrush = ending ? YellowBrush : GreenBrush;
                glow.Opacity = 0;
            }
            else
            {
                // Down: dim the icon and pulse a red frame until it's reapplied.
                dim.Visibility = Visibility.Visible;
                buffBar.Visibility = Visibility.Collapsed;
                countdown.Visibility = Visibility.Collapsed;
                frame.BorderBrush = RedBrush;
                glow.Color = Red;
                glow.Opacity = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(now.TimeOfDay.TotalSeconds * Math.PI * 2));
            }
        }

        private void ShowCountdown(TimeSpan left, bool warn)
        {
            countdown.Text = FormatTimeLeft(left);
            countdown.Foreground = warn ? YellowBrush : Brushes.White;
            countdown.Visibility = Visibility.Visible;
        }

        private static double Fraction(TimedItemState s) =>
            s.Duration.TotalMilliseconds > 0 ? Math.Clamp(s.TimeLeft.TotalMilliseconds / s.Duration.TotalMilliseconds, 0, 1) : 0;

        /// <summary>
        /// Dark pie over the part of the cooldown still remaining (clockwise from 12 o'clock back to 12),
        /// so the bright part grows clockwise as the cooldown runs down.
        /// </summary>
        private static Geometry BuildSweep(double remaining, double size)
        {
            if (remaining >= 0.999) return new RectangleGeometry(new Rect(0, 0, size, size));
            if (remaining <= 0.001) return Geometry.Empty;

            double c = size / 2, r = size; // radius past the corners so the square is fully covered
            double a = (1 - remaining) * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Point(c, c), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), false));
            figure.Segments.Add(new ArcSegment(new Point(c, c - r), new Size(r, r), 0, remaining > 0.5, SweepDirection.Clockwise, false));
            var g = new PathGeometry(new[] { figure });
            g.Freeze();
            return g;
        }

        private static string FormatTimeLeft(TimeSpan timeLeft)
        {
            var s = Math.Max(0, timeLeft.TotalSeconds);
            if (s >= 60) return $"{Math.Ceiling(s / 60).ToString(CultureInfo.InvariantCulture)}m";
            // Whole seconds, like the game's own buff bar; one decimal only in the last 2 seconds (owner, 2026-11-21).
            if (s >= 2) return Math.Ceiling(s).ToString(CultureInfo.InvariantCulture);
            return s.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void ApplySize()
        {
            iconSize = Math.Clamp(settings.Current.TrackerIconSize, 24, 96);
            iconArea.Width = iconArea.Height = iconSize;
            countdown.FontSize = Math.Max(10, iconSize * 0.36);
            fallbackLetter.FontSize = Math.Max(10, iconSize * 0.45);
        }

        private void LoadIcon(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            var localPath = SkillIconCache.Instance.GetLocalPathOrStartDownload(
                url, onDownloaded: () => Dispatcher.BeginInvoke(() => LoadIcon(url)));
            if (localPath is null) return; // downloading; called again when it lands

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't keep the cache file locked
                bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;
                fallbackLetter.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // Broken cache file: keep the letter fallback.
            }
        }

        private void RestorePosition()
        {
            double outer = iconSize + Pad * 2;
            var saved = settings.Current.TrackedSkills.FirstOrDefault(t => t.SkillId == skillId);

            if (saved?.Left is double left && saved.Top is double top && IsOnScreen(left, top, outer))
            {
                Left = left;
                Top = top;
                return;
            }

            // Not placed yet (or its monitor is gone): line up a little below the screen centre.
            Left = SystemParameters.PrimaryScreenWidth / 2 - outer / 2 + (defaultSlot - 2) * (outer + 8);
            Top = SystemParameters.PrimaryScreenHeight / 2 + 120;
        }

        private static bool IsOnScreen(double left, double top, double size) =>
            left + size > SystemParameters.VirtualScreenLeft
            && top + size > SystemParameters.VirtualScreenTop
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;

            OverlayManager.TryDragMove(this); // returns when the button is released

            double left = Left, top = Top;
            settings.Update(s =>
            {
                var entry = s.TrackedSkills.FirstOrDefault(t => t.SkillId == skillId);
                if (entry is null) return;
                entry.Left = left;
                entry.Top = top;
            });
        }

        private void OnSettingsChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (Math.Abs(Math.Clamp(settings.Current.TrackerIconSize, 24, 96) - iconSize) > 0.1) ApplySize();
            });

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
