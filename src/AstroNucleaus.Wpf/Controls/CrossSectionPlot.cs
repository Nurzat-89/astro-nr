using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Wpf.Controls;

/// <summary>
/// Log-log plot of a tabulated function (cross section in barns versus energy in eV).
/// Points with non-positive x or y are skipped.
/// </summary>
public sealed class CrossSectionPlot : FrameworkElement
{
    public static readonly DependencyProperty FunctionProperty = DependencyProperty.Register(
        nameof(Function),
        typeof(TabulatedFunction),
        typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty XAxisTitleProperty = DependencyProperty.Register(
        nameof(XAxisTitle), typeof(string), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata("Energy (eV)", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YAxisTitleProperty = DependencyProperty.Register(
        nameof(YAxisTitle), typeof(string), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata("Cross section (b)", FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen AxisPen = Freeze(new Pen(Brushes.DimGray, 1));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), 1));
    private static readonly Pen CurvePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA8)), 1.4));
    private static readonly Typeface LabelFont = new("Segoe UI");

    private const double LeftMargin = 64;
    private const double RightMargin = 16;
    private const double TopMargin = 12;
    private const double BottomMargin = 44;

    public TabulatedFunction? Function
    {
        get => (TabulatedFunction?)GetValue(FunctionProperty);
        set => SetValue(FunctionProperty, value);
    }

    public string XAxisTitle
    {
        get => (string)GetValue(XAxisTitleProperty);
        set => SetValue(XAxisTitleProperty, value);
    }

    public string YAxisTitle
    {
        get => (string)GetValue(YAxisTitleProperty);
        set => SetValue(YAxisTitleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(Brushes.White, null, bounds);

        var plot = new Rect(
            LeftMargin,
            TopMargin,
            Math.Max(0, bounds.Width - LeftMargin - RightMargin),
            Math.Max(0, bounds.Height - TopMargin - BottomMargin));

        if (plot.Width < 20 || plot.Height < 20)
            return;

        var function = Function;
        if (function is null)
        {
            DrawCenteredMessage(dc, bounds, "Select a cross section");
            return;
        }

        if (!TryGetLogRange(function, out var x0, out var x1, out var y0, out var y1))
        {
            DrawCenteredMessage(dc, bounds, "No positive values to show on a log scale");
            return;
        }

        double MapX(double x) => plot.Left + (Math.Log10(x) - x0) / (x1 - x0) * plot.Width;
        double MapY(double y) => plot.Bottom - (Math.Log10(y) - y0) / (y1 - y0) * plot.Height;

        DrawGridAndLabels(dc, plot, x0, x1, y0, y1);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var inFigure = false;
            for (var i = 0; i < function.Count; i++)
            {
                var x = function.X[i];
                var y = function.Y[i];
                if (x <= 0 || y <= 0)
                {
                    inFigure = false;
                    continue;
                }

                var point = new Point(MapX(x), MapY(y));
                if (!inFigure)
                {
                    ctx.BeginFigure(point, isFilled: false, isClosed: false);
                    inFigure = true;
                }
                else
                {
                    ctx.LineTo(point, isStroked: true, isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();
        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawGeometry(null, CurvePen, geometry);
        dc.Pop();

        dc.DrawRectangle(null, AxisPen, plot);
    }

    private void DrawGridAndLabels(DrawingContext dc, Rect plot, double x0, double x1, double y0, double y1)
    {
        var xStep = DecadeStep(x1 - x0, plot.Width / 56);
        for (var d = x0; d <= x1 + 1e-9; d += xStep)
        {
            var x = plot.Left + (d - x0) / (x1 - x0) * plot.Width;
            dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Text(FormatDecade(d), 11);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 4));
        }

        var yStep = DecadeStep(y1 - y0, plot.Height / 24);
        for (var d = y0; d <= y1 + 1e-9; d += yStep)
        {
            var y = plot.Bottom - (d - y0) / (y1 - y0) * plot.Height;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(FormatDecade(d), 11);
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
        }

        var xTitle = Text(XAxisTitle, 12);
        dc.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 22));

        var yTitle = Text(YAxisTitle, 12);
        dc.PushTransform(new RotateTransform(-90, 14, plot.Top + plot.Height / 2));
        dc.DrawText(yTitle, new Point(14 - yTitle.Width / 2, plot.Top + plot.Height / 2 - yTitle.Height / 2));
        dc.Pop();
    }

    private static bool TryGetLogRange(TabulatedFunction function, out double x0, out double x1, out double y0, out double y1)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (var i = 0; i < function.Count; i++)
        {
            var x = function.X[i];
            var y = function.Y[i];
            if (x <= 0 || y <= 0)
                continue;

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        if (minX > maxX)
        {
            x0 = x1 = y0 = y1 = 0;
            return false;
        }

        x0 = Math.Floor(Math.Log10(minX));
        x1 = Math.Ceiling(Math.Log10(maxX));
        y0 = Math.Floor(Math.Log10(minY));
        y1 = Math.Ceiling(Math.Log10(maxY));
        if (x1 <= x0) x1 = x0 + 1;
        if (y1 <= y0) y1 = y0 + 1;
        return true;
    }

    private static double DecadeStep(double decades, double maxLabels) =>
        Math.Max(1, Math.Ceiling(decades / Math.Max(1, Math.Floor(maxLabels))));

    private static string FormatDecade(double exponent) =>
        exponent switch
        {
            0 => "1",
            1 => "10",
            _ => "1E" + exponent.ToString("0", CultureInfo.InvariantCulture),
        };

    private void DrawCenteredMessage(DrawingContext dc, Rect bounds, string message)
    {
        var text = Text(message, 13);
        dc.DrawText(text, new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
    }

    private FormattedText Text(string text, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFont, size, Brushes.DimGray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Pen Freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
