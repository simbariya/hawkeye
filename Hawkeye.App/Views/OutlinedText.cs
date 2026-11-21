using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Hawkeye.App.Views
{
    /// <summary>
    /// Text with a solid black outline, like the game's own buff timers. A drop shadow alone vanished on
    /// bright icons (Marking Shot: white number on a white icon, owner 2026-11-21).
    /// Same members the tracker icon used on its TextBlock: Text, Foreground, FontSize.
    /// </summary>
    public sealed class OutlinedText : FrameworkElement
    {
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Pen OutlinePen = MakePen();

        private string text = string.Empty;
        private Brush foreground = Brushes.White;
        private double fontSize = 14;
        private FormattedText? formatted;

        public string Text
        {
            get => text;
            set { if (value == text) return; text = value; Invalidate(); }
        }

        public Brush Foreground
        {
            get => foreground;
            set { if (ReferenceEquals(value, foreground)) return; foreground = value; InvalidateVisual(); }
        }

        public double FontSize
        {
            get => fontSize;
            set { if (value == fontSize) return; fontSize = value; Invalidate(); }
        }

        private static Pen MakePen()
        {
            var pen = new Pen(Brushes.Black, 3.2) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        private void Invalidate()
        {
            formatted = null;
            InvalidateMeasure();
            InvalidateVisual();
        }

        private FormattedText Format() => formatted ??= new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, fontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected override Size MeasureOverride(Size availableSize)
        {
            var ft = Format();
            return new Size(ft.Width + 4, ft.Height + 2);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ft = Format();
            var origin = new Point((RenderSize.Width - ft.Width) / 2, (RenderSize.Height - ft.Height) / 2);
            var geometry = ft.BuildGeometry(origin);
            dc.DrawGeometry(null, OutlinePen, geometry);   // outline first, then the fill on top
            dc.DrawGeometry(foreground, null, geometry);
        }
    }
}
