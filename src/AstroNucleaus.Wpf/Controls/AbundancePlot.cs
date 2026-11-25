using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Wpf.Controls;

/// <summary>
/// Abundance distribution: number abundance relative to the initial ⁵⁶Fe (log scale) versus mass number.
/// Isotopes of one element are connected and labelled with the element symbol. Hovering shows the value.
/// </summary>
public sealed class AbundancePlot : FrameworkElement
{
    public static readonly DependencyProperty AbundancesProperty = DependencyProperty.Register(
        nameof(Abundances), typeof(IReadOnlyList<NuclideAbundance>), typeof(AbundancePlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowDecayedProperty = DependencyProperty.Register(
        nameof(ShowDecayed), typeof(bool), typeof(AbundancePlot),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DecadesProperty = DependencyProperty.Register(
        nameof(Decades), typeof(int), typeof(AbundancePlot),
        new FrameworkPropertyMetadata(10, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 70;
    private const double RightMargin = 18;
    private const double TopMargin = 14;
    private const double BottomMargin = 50;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x1E, 0x6F, 0xE8), Color.FromRgb(0xE0, 0x6C, 0x1F), Color.FromRgb(0x2E, 0x9D, 0x5B),
        Color.FromRgb(0xC8, 0x37, 0x2D), Color.FromRgb(0x7B, 0x4F, 0xC9), Color.FromRgb(0x0F, 0x8B, 0x9C),
        Color.FromRgb(0xB5, 0x8A, 0x00), Color.FromRgb(0xC2, 0x3B, 0x8E),
    ];

    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3E, 0x48, 0x57)));
    private static readonly Brush TitleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x23, 0x30)));
    private static readonly Brush ReadoutBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x23, 0x30)));
    private static readonly Pen AxisPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xD8)), 1));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0)), 1));
    private static readonly Typeface LabelFont = new("Segoe UI");
    private static readonly Typeface BoldFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly List<(Point Position, NuclideAbundance Item, double Value)> _hitPoints = [];
    private Point? _hover;

    public AbundancePlot()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<NuclideAbundance>? Abundances
    {
        get => (IReadOnlyList<NuclideAbundance>?)GetValue(AbundancesProperty);
        set => SetValue(AbundancesProperty, value);
    }

    /// <summary>True: abundances after short-lived nuclei decayed; false: at the end of He burning.</summary>
    public bool ShowDecayed
    {
        get => (bool)GetValue(ShowDecayedProperty);
        set => SetValue(ShowDecayedProperty, value);
    }

    /// <summary>Number of decades shown below the largest value.</summary>
    public int Decades
    {
        get => (int)GetValue(DecadesProperty);
        set => SetValue(DecadesProperty, value);
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
        _hitPoints.Clear();

        var plot = new Rect(LeftMargin, TopMargin,
            Math.Max(0, bounds.Width - LeftMargin - RightMargin), Math.Max(0, bounds.Height - TopMargin - BottomMargin));
        if (plot.Width < 60 || plot.Height < 60)
            return;

        var points = (Abundances ?? [])
            .Select(a => (Item: a, Value: ShowDecayed ? a.DecayedRelativeToInitialFe56 : a.RelativeToInitialFe56))
            .Where(p => p.Value > 0 && (!ShowDecayed || p.Item.IsStable || p.Item.HalfLifeSeconds > SProcessCalculator.DecayCutoffYears * 3.15576e7))
            .ToList();
        if (points.Count == 0)
        {
            var message = Text(Abundances is null ? "Run a calculation to see the abundances" : "No abundances to show", 14, TextBrush, LabelFont);
            dc.DrawText(message, new Point((bounds.Width - message.Width) / 2, (bounds.Height - message.Height) / 2));
            return;
        }

        var top = Math.Ceiling(Math.Log10(points.Max(p => p.Value)));
        var bottom = top - Math.Max(2, Decades);
        points = points.Where(p => Math.Log10(p.Value) >= bottom).ToList();
        var minA = points.Min(p => p.Item.Nuclide.A) - 1;
        var maxA = points.Max(p => p.Item.Nuclide.A) + 1;

        double MapX(double a) => plot.Left + (a - minA) / (maxA - minA) * plot.Width;
        double MapY(double v) => plot.Bottom - (Math.Log10(v) - bottom) / (top - bottom) * plot.Height;

        // Grid and axes.
        for (var d = bottom; d <= top; d++)
        {
            var y = plot.Bottom - (d - bottom) / (top - bottom) * plot.Height;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(d == 0 ? "1" : "1E" + d.ToString("0", CultureInfo.InvariantCulture), 12, TextBrush, LabelFont);
            dc.DrawText(label, new Point(plot.Left - label.Width - 8, y - label.Height / 2));
        }

        var step = (maxA - minA) > 40 ? 5 : 2;
        for (var a = (int)Math.Ceiling(minA / (double)step) * step; a <= maxA; a += step)
        {
            var x = MapX(a);
            dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Text(a.ToString(CultureInfo.InvariantCulture), 12, TextBrush, LabelFont);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 6));
        }

        dc.DrawRectangle(null, AxisPen, plot);
        var xTitle = Text("Mass number A", 14, TitleBrush, BoldFont);
        dc.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 26));
        var yTitle = Text(ShowDecayed ? "Y / Y₀(⁵⁶Fe), decayed" : "Y / Y₀(⁵⁶Fe), end of He burning", 14, TitleBrush, BoldFont);
        dc.PushTransform(new RotateTransform(-90, 16, plot.Top + plot.Height / 2));
        dc.DrawText(yTitle, new Point(16 - yTitle.Width / 2, plot.Top + plot.Height / 2 - yTitle.Height / 2));
        dc.Pop();

        // Elements: connected isotopes plus a label above the most abundant one.
        dc.PushClip(new RectangleGeometry(plot));
        foreach (var element in points.GroupBy(p => p.Item.Nuclide.Z).OrderBy(g => g.Key))
        {
            var color = Palette[element.Key % Palette.Length];
            var brush = Frozen(new SolidColorBrush(color));
            var pen = Frozen(new Pen(brush, 1.4));
            var isotopes = element.OrderBy(p => p.Item.Nuclide.A).ToList();

            for (var i = 1; i < isotopes.Count; i++)
            {
                var a0 = isotopes[i - 1];
                var a1 = isotopes[i];
                if (a1.Item.Nuclide.A - a0.Item.Nuclide.A <= 2)
                {
                    dc.DrawLine(pen,
                        new Point(MapX(a0.Item.Nuclide.A), MapY(a0.Value)),
                        new Point(MapX(a1.Item.Nuclide.A), MapY(a1.Value)));
                }
            }

            foreach (var isotope in isotopes)
            {
                var position = new Point(MapX(isotope.Item.Nuclide.A), MapY(isotope.Value));
                var fill = isotope.Item.IsStable ? brush : Brushes.White;
                dc.DrawEllipse(fill, pen, position, 3.2, 3.2);
                _hitPoints.Add((position, isotope.Item, isotope.Value));
            }

            var peak = isotopes.MaxBy(p => p.Value);
            var symbol = Text(peak.Item.Nuclide.Symbol, 12, brush, BoldFont);
            var labelPosition = new Point(MapX(peak.Item.Nuclide.A) - symbol.Width / 2, MapY(peak.Value) - symbol.Height - 4);
            dc.DrawText(symbol, labelPosition);
        }

        dc.Pop();

        if (_hover is { } hover && plot.Contains(hover) && _hitPoints.Count > 0)
        {
            var nearest = _hitPoints.MinBy(p => (p.Position - hover).LengthSquared);
            if ((nearest.Position - hover).Length < 18)
            {
                dc.DrawEllipse(null, new Pen(TitleBrush, 1.5), nearest.Position, 6, 6);
                var item = nearest.Item;
                var halfLife = item.IsStable ? "stable" : $"T½ = {FormatTime(item.HalfLifeSeconds)}";
                var text = Text(
                    $"{item.Nuclide}  ({halfLife})\nY/Y₀(⁵⁶Fe) = {nearest.Value:0.###E+00}\nMACS = {item.CaptureBarns * 1000:0.###} mb",
                    12, Brushes.White, LabelFont);
                var box = new Rect(nearest.Position.X + 12, nearest.Position.Y + 12, text.Width + 16, text.Height + 10);
                if (box.Right > plot.Right) box.X = nearest.Position.X - box.Width - 12;
                if (box.Bottom > plot.Bottom) box.Y = nearest.Position.Y - box.Height - 12;
                dc.DrawRoundedRectangle(ReadoutBackground, null, box, 5, 5);
                dc.DrawText(text, new Point(box.X + 8, box.Y + 5));
            }
        }
    }

    private static string FormatTime(double seconds) => seconds switch
    {
        < 60 => $"{seconds:0.##} s",
        < 86400 => $"{seconds / 3600:0.##} h",
        < 3.15576e7 => $"{seconds / 86400:0.##} d",
        _ => $"{seconds / 3.15576e7:0.###E+00} yr",
    };

    private FormattedText Text(string text, double size, Brush brush, Typeface font) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
