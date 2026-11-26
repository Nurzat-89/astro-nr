using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Wpf.Controls;

/// <summary>One square of the chart of nuclides.</summary>
/// <param name="Details">Extra lines for the hover readout (half-life, cross section, …).</param>
public sealed record NuclideCell(Nuclide Nuclide, double Value, bool IsStable, string Details);

/// <summary>
/// Heatmap on the chart of nuclides (N horizontally, Z vertically): colour shows the abundance on a log scale.
/// Stable nuclei are outlined; magic numbers 28, 50, 82, 126 are marked. With <see cref="ScaleTop"/> set,
/// the colour scale stays fixed, so an animation shows true changes of concentration.
/// </summary>
public sealed class NuclideChartPlot : FrameworkElement
{
    public static readonly DependencyProperty CellsProperty = DependencyProperty.Register(
        nameof(Cells), typeof(IReadOnlyList<NuclideCell>), typeof(NuclideChartPlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScaleTopProperty = DependencyProperty.Register(
        nameof(ScaleTop), typeof(double), typeof(NuclideChartPlot),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DecadesProperty = DependencyProperty.Register(
        nameof(Decades), typeof(int), typeof(NuclideChartPlot),
        new FrameworkPropertyMetadata(8, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueLabelProperty = DependencyProperty.Register(
        nameof(ValueLabel), typeof(string), typeof(NuclideChartPlot),
        new FrameworkPropertyMetadata("Y/Y₀", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyMessageProperty = DependencyProperty.Register(
        nameof(EmptyMessage), typeof(string), typeof(NuclideChartPlot),
        new FrameworkPropertyMetadata("Run a calculation to see the nuclides", FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 48;
    private const double BottomMargin = 40;
    private const double EdgeMargin = 12;
    private static readonly int[] MagicNumbers = [28, 50, 82, 126];

    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3E, 0x48, 0x57)));
    private static readonly Brush ReadoutBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x23, 0x30)));
    private static readonly Pen StablePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1B, 0x23, 0x30)), 1));
    private static readonly Pen MagicPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xC8, 0x37, 0x2D)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Brush EmptyBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF6)));
    private static readonly Color[] Stops =
    [
        Color.FromRgb(0xFF, 0xF4, 0xC2), Color.FromRgb(0xF6, 0x9A, 0x3C), Color.FromRgb(0xC8, 0x37, 0x2D), Color.FromRgb(0x1E, 0x3F, 0x9C),
    ];

    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly Dictionary<int, Brush> _brushCache = [];
    private Point? _hover;

    public NuclideChartPlot()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<NuclideCell>? Cells
    {
        get => (IReadOnlyList<NuclideCell>?)GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    /// <summary>log₁₀ of the value at the top of the colour scale; NaN uses the largest value shown.</summary>
    public double ScaleTop
    {
        get => (double)GetValue(ScaleTopProperty);
        set => SetValue(ScaleTopProperty, value);
    }

    public int Decades
    {
        get => (int)GetValue(DecadesProperty);
        set => SetValue(DecadesProperty, value);
    }

    public string ValueLabel
    {
        get => (string)GetValue(ValueLabelProperty);
        set => SetValue(ValueLabelProperty, value);
    }

    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
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
        var cells = Cells;
        if (cells is null || cells.Count == 0)
        {
            var message = Text(EmptyMessage, 14);
            dc.DrawText(message, new Point((bounds.Width - message.Width) / 2, (bounds.Height - message.Height) / 2));
            return;
        }

        var minN = cells.Min(c => c.Nuclide.N) - 1;
        var maxN = cells.Max(c => c.Nuclide.N) + 1;
        var minZ = cells.Min(c => c.Nuclide.Z) - 1;
        var maxZ = cells.Max(c => c.Nuclide.Z) + 1;

        var area = new Rect(LeftMargin, EdgeMargin, Math.Max(0, bounds.Width - LeftMargin - EdgeMargin), Math.Max(0, bounds.Height - EdgeMargin - BottomMargin));
        var cell = Math.Min(area.Width / (maxN - minN + 1), area.Height / (maxZ - minZ + 1));
        if (cell < 1)
            return;

        var width = cell * (maxN - minN + 1);
        var height = cell * (maxZ - minZ + 1);
        var origin = new Point(area.Left, area.Bottom); // bottom-left corner of the chart
        Rect CellRect(int n, int z) => new(origin.X + (n - minN) * cell, origin.Y - (z - minZ + 1) * cell, cell, cell);

        var top = double.IsNaN(ScaleTop) ? Math.Log10(Math.Max(cells.Max(c => c.Value), 1e-300)) : ScaleTop;
        var decades = Math.Max(1, Decades);
        var gap = cell > 8 ? 1.0 : 0.0;

        NuclideCell? hovered = null;
        foreach (var item in cells)
        {
            var rect = CellRect(item.Nuclide.N, item.Nuclide.Z);
            var inner = new Rect(rect.X + gap / 2, rect.Y + gap / 2, Math.Max(0.5, rect.Width - gap), Math.Max(0.5, rect.Height - gap));
            var fill = item.Value > 0 && Math.Log10(item.Value) > top - decades
                ? BrushFor((Math.Log10(item.Value) - (top - decades)) / decades)
                : EmptyBrush;
            dc.DrawRectangle(fill, item.IsStable ? StablePen : null, inner);

            if (cell >= 22)
            {
                var label = Text(item.Nuclide.A.ToString(CultureInfo.InvariantCulture), Math.Min(11, cell * 0.38));
                dc.DrawText(label, new Point(rect.X + (rect.Width - label.Width) / 2, rect.Y + (rect.Height - label.Height) / 2));
            }

            if (_hover is { } p && rect.Contains(p))
                hovered = item;
        }

        // Element symbols at the left of each row.
        if (cell >= 10)
        {
            foreach (var row in cells.GroupBy(c => c.Nuclide.Z))
            {
                var first = row.MinBy(c => c.Nuclide.N)!;
                var rect = CellRect(first.Nuclide.N, row.Key);
                var label = Text(first.Nuclide.Symbol, Math.Min(12, cell * 0.6), TextBrush, Bold);
                dc.DrawText(label, new Point(rect.Left - label.Width - 3, rect.Top + (rect.Height - label.Height) / 2));
            }
        }

        foreach (var magic in MagicNumbers)
        {
            if (magic > minN && magic < maxN)
            {
                var x = origin.X + (magic - minN + 0.5) * cell;
                dc.DrawLine(MagicPen, new Point(x, origin.Y), new Point(x, origin.Y - height));
                var label = Text($"N={magic}", 11);
                dc.DrawText(label, new Point(x - label.Width / 2, origin.Y + 4));
            }

            if (magic > minZ && magic < maxZ)
            {
                var y = origin.Y - (magic - minZ + 0.5) * cell;
                dc.DrawLine(MagicPen, new Point(origin.X, y), new Point(origin.X + width, y));
                var label = Text($"Z={magic}", 11);
                dc.DrawText(label, new Point(origin.X - label.Width - 4, y - label.Height / 2));
            }
        }

        var xTitle = Text("Neutron number N", 13);
        dc.DrawText(xTitle, new Point(origin.X + (width - xTitle.Width) / 2, origin.Y + 20));

        // Colour scale.
        var scale = new Rect(Math.Max(origin.X, bounds.Right - EdgeMargin - 170), EdgeMargin + 4, 150, 10);
        for (var i = 0; i < 150; i++)
            dc.DrawRectangle(BrushFor(i / 149.0), null, new Rect(scale.Left + i, scale.Top, 1, scale.Height));
        var low = Text($"1E{top - decades:0}", 10);
        var high = Text($"1E{top:0}", 10);
        var caption = Text(ValueLabel, 10);
        dc.DrawText(low, new Point(scale.Left, scale.Bottom + 2));
        dc.DrawText(high, new Point(scale.Right - high.Width, scale.Bottom + 2));
        dc.DrawText(caption, new Point(scale.Left + (scale.Width - caption.Width) / 2, scale.Bottom + 2));

        if (hovered is not null && _hover is { } hover)
        {
            var text = Text($"{hovered.Nuclide} (Z = {hovered.Nuclide.Z}, N = {hovered.Nuclide.N})\n{ValueLabel} = {hovered.Value:0.###E+00}\n{hovered.Details}", 12, Brushes.White);
            var box = new Rect(hover.X + 12, hover.Y + 12, text.Width + 16, text.Height + 10);
            if (box.Right > bounds.Right) box.X = hover.X - box.Width - 12;
            if (box.Bottom > bounds.Bottom) box.Y = hover.Y - box.Height - 12;
            dc.DrawRoundedRectangle(ReadoutBackground, null, box, 5, 5);
            dc.DrawText(text, new Point(box.X + 8, box.Y + 5));
        }
    }

    /// <summary>Light yellow → orange → red → dark blue as the abundance rises (256 cached shades).</summary>
    private Brush BrushFor(double t)
    {
        var key = (int)Math.Round(Math.Clamp(t, 0, 1) * 255);
        if (_brushCache.TryGetValue(key, out var cached))
            return cached;

        var position = key / 255.0 * (Stops.Length - 1);
        var i = Math.Min((int)position, Stops.Length - 2);
        var f = position - i;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * f);
        var brush = Frozen(new SolidColorBrush(Color.FromRgb(
            Mix(Stops[i].R, Stops[i + 1].R), Mix(Stops[i].G, Stops[i + 1].G), Mix(Stops[i].B, Stops[i + 1].B))));
        _brushCache[key] = brush;
        return brush;
    }

    private FormattedText Text(string text, double size, Brush? brush = null, Typeface? font = null) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font ?? Font, size, brush ?? TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
