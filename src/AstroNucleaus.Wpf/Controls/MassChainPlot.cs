using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AstroNucleaus.Domain.RProcess;

namespace AstroNucleaus.Wpf.Controls;

/// <summary>
/// Abundance per mass number A (summed over isobars, log scale): after decay as the main curve,
/// at freeze-out as a thin grey line.
/// </summary>
public sealed class MassChainPlot : FrameworkElement
{
    public static readonly DependencyProperty ChainsProperty = DependencyProperty.Register(
        nameof(Chains), typeof(IReadOnlyList<MassChainAbundance>), typeof(MassChainPlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 70;
    private const double RightMargin = 18;
    private const double TopMargin = 14;
    private const double BottomMargin = 50;
    private const int Decades = 9;

    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3E, 0x48, 0x57)));
    private static readonly Brush TitleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x23, 0x30)));
    private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xE8)));
    private static readonly Brush ReadoutBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x23, 0x30)));
    private static readonly Pen AccentPen = Frozen(new Pen(AccentBrush, 1.6));
    private static readonly Pen FreezePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xA9, 0xB1, 0xBD)), 1.2));
    private static readonly Pen AxisPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xD8)), 1));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0)), 1));
    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private Point? _hover;

    public MassChainPlot()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<MassChainAbundance>? Chains
    {
        get => (IReadOnlyList<MassChainAbundance>?)GetValue(ChainsProperty);
        set => SetValue(ChainsProperty, value);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(Brushes.White, null, bounds);
        var plot = new Rect(LeftMargin, TopMargin, Math.Max(0, bounds.Width - LeftMargin - RightMargin), Math.Max(0, bounds.Height - TopMargin - BottomMargin));
        var chains = Chains?.Where(c => c.Decayed > 0 || c.AtFreezeOut > 0).ToList();
        if (chains is null || chains.Count == 0 || plot.Width < 60 || plot.Height < 60)
        {
            var message = Text("Run a calculation to see the abundances", 14, TextBrush, Font);
            dc.DrawText(message, new Point((bounds.Width - message.Width) / 2, (bounds.Height - message.Height) / 2));
            return;
        }

        var top = Math.Ceiling(Math.Log10(chains.Max(c => Math.Max(c.Decayed, c.AtFreezeOut))));
        var bottom = top - Decades;
        var minA = chains.Min(c => c.A) - 2;
        var maxA = chains.Max(c => c.A) + 2;
        double X(double a) => plot.Left + (a - minA) / (maxA - minA) * plot.Width;
        double Y(double v) => plot.Bottom - (Math.Log10(Math.Max(v, Math.Pow(10, bottom))) - bottom) / (top - bottom) * plot.Height;

        for (var d = bottom; d <= top; d++)
        {
            var y = Y(Math.Pow(10, d));
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(d == 0 ? "1" : $"1E{d:0}", 12, TextBrush, Font);
            dc.DrawText(label, new Point(plot.Left - label.Width - 8, y - label.Height / 2));
        }

        var step = maxA - minA > 120 ? 20 : maxA - minA > 50 ? 10 : 5;
        for (var a = (int)Math.Ceiling(minA / (double)step) * step; a <= maxA; a += step)
        {
            dc.DrawLine(GridPen, new Point(X(a), plot.Top), new Point(X(a), plot.Bottom));
            var label = Text(a.ToString(CultureInfo.InvariantCulture), 12, TextBrush, Font);
            dc.DrawText(label, new Point(X(a) - label.Width / 2, plot.Bottom + 6));
        }

        dc.DrawRectangle(null, AxisPen, plot);
        var xTitle = Text("Mass number A", 14, TitleBrush, Bold);
        dc.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 26));
        var yTitle = Text("Y(A) / Y₀(seed)", 14, TitleBrush, Bold);
        dc.PushTransform(new RotateTransform(-90, 16, plot.Top + plot.Height / 2));
        dc.DrawText(yTitle, new Point(16 - yTitle.Width / 2, plot.Top + plot.Height / 2 - yTitle.Height / 2));
        dc.Pop();

        dc.PushClip(new RectangleGeometry(plot));
        DrawSeries(dc, chains.Where(c => c.AtFreezeOut > 0).Select(c => new Point(X(c.A), Y(c.AtFreezeOut))).ToList(), FreezePen, null);
        DrawSeries(dc, chains.Where(c => c.Decayed > 0).Select(c => new Point(X(c.A), Y(c.Decayed))).ToList(), AccentPen, AccentBrush);
        dc.Pop();

        var legendDecayed = Text("after decay", 12, AccentBrush, Font);
        var legendFreeze = Text("at freeze-out", 12, TextBrush, Font);
        dc.DrawText(legendDecayed, new Point(plot.Right - legendDecayed.Width - 8, plot.Top + 6));
        dc.DrawText(legendFreeze, new Point(plot.Right - legendFreeze.Width - 8, plot.Top + 24));

        if (_hover is { } hover && plot.Contains(hover))
        {
            var a = (int)Math.Round(minA + (hover.X - plot.Left) / plot.Width * (maxA - minA));
            if (chains.FirstOrDefault(c => c.A == a) is { } chain)
            {
                dc.DrawLine(GridPen, new Point(X(a), plot.Top), new Point(X(a), plot.Bottom));
                var text = Text($"A = {a}\nafter decay: {chain.Decayed:0.###E+00}\nfreeze-out: {chain.AtFreezeOut:0.###E+00}", 12, Brushes.White, Font);
                var box = new Rect(hover.X + 12, hover.Y + 12, text.Width + 16, text.Height + 10);
                if (box.Right > plot.Right) box.X = hover.X - box.Width - 12;
                if (box.Bottom > plot.Bottom) box.Y = hover.Y - box.Height - 12;
                dc.DrawRoundedRectangle(ReadoutBackground, null, box, 5, 5);
                dc.DrawText(text, new Point(box.X + 8, box.Y + 5));
            }
        }
    }

    private static void DrawSeries(DrawingContext dc, List<Point> points, Pen pen, Brush? marker)
    {
        if (points.Count == 0)
            return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        if (marker is not null && points.Count < 400)
        {
            foreach (var p in points)
                dc.DrawEllipse(marker, null, p, 2.2, 2.2);
        }
    }

    private FormattedText Text(string text, double size, Brush brush, Typeface font) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
