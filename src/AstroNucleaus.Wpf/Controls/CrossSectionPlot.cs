using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Wpf.Controls;

/// <summary>
/// Interactive log-log plot of a tabulated function (cross section in barns versus energy in eV).
/// Mouse wheel zooms around the cursor, dragging pans, double-click resets the view.
/// Points with non-positive x or y are skipped.
/// </summary>
public sealed class CrossSectionPlot : FrameworkElement
{
    public static readonly DependencyProperty FunctionProperty = DependencyProperty.Register(
        nameof(Function), typeof(TabulatedFunction), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((CrossSectionPlot)d).ResetView()));

    public static readonly DependencyProperty XAxisTitleProperty = DependencyProperty.Register(
        nameof(XAxisTitle), typeof(string), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata("Energy (eV)", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YAxisTitleProperty = DependencyProperty.Register(
        nameof(YAxisTitle), typeof(string), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata("Cross section (b)", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowMarkersProperty = DependencyProperty.Register(
        nameof(ShowMarkers), typeof(bool), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowReadoutProperty = DependencyProperty.Register(
        nameof(ShowReadout), typeof(bool), typeof(CrossSectionPlot),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 66;
    private const double RightMargin = 18;
    private const double TopMargin = 10;
    private const double BottomMargin = 50;
    private const int MaxMarkers = 4000;

    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3E, 0x48, 0x57)));
    private static readonly Brush TitleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x23, 0x30)));
    private static readonly Brush CurveBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xE8)));
    private static readonly Brush ReadoutBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x23, 0x30)));
    private static readonly Pen AxisPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xD8)), 1));
    private static readonly Pen MajorGridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xED)), 1));
    private static readonly Pen MinorGridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7)), 1));
    private static readonly Pen CurvePen = Frozen(new Pen(CurveBrush, 1.6) { LineJoin = PenLineJoin.Round });
    private static readonly Pen CursorPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA3)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Typeface LabelFont = new("Segoe UI");
    private static readonly Typeface TitleFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // Visible range in log10 units; null means "fit to data".
    private (double X0, double X1, double Y0, double Y1)? _view;
    private Point? _dragStart;
    private (double X0, double X1, double Y0, double Y1) _dragView;
    private Point? _hover;

    public CrossSectionPlot()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

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

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool ShowMarkers
    {
        get => (bool)GetValue(ShowMarkersProperty);
        set => SetValue(ShowMarkersProperty, value);
    }

    public bool ShowReadout
    {
        get => (bool)GetValue(ShowReadoutProperty);
        set => SetValue(ShowReadoutProperty, value);
    }

    public void ZoomIn() => ZoomAround(null, 0.5);

    public void ZoomOut() => ZoomAround(null, 2.0);

    public void ResetView()
    {
        _view = null;
        InvalidateVisual();
    }

    /// <summary>Saves the current view as a PNG image.</summary>
    public void SaveAsPng(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            Render(dc, includeReadout: false);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAround(e.GetPosition(this), e.Delta > 0 ? 0.8 : 1.25);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        if (CurrentView() is { } view)
        {
            _dragStart = e.GetPosition(this);
            _dragView = view;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragStart is not null)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            Cursor = Cursors.Cross;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        _hover = position;

        if (_dragStart is { } start)
        {
            var plot = PlotArea();
            var (x0, x1, y0, y1) = _dragView;
            var dx = (position.X - start.X) / plot.Width * (x1 - x0);
            var dy = (position.Y - start.Y) / plot.Height * (y1 - y0);
            _view = (x0 - dx, x1 - dx, y0 + dy, y1 + dy);
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) => Render(dc, includeReadout: true);

    private void Render(DrawingContext dc, bool includeReadout)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(Brushes.White, null, bounds);

        var plot = PlotArea();
        if (plot.Width < 40 || plot.Height < 40)
            return;

        var function = Function;
        if (function is null)
        {
            DrawCentered(dc, bounds, "Select a cross section to plot it");
            return;
        }

        if (CurrentView() is not { } view)
        {
            DrawCentered(dc, bounds, "No positive values to show on a log-log scale");
            return;
        }

        var (x0, x1, y0, y1) = view;
        double MapX(double logX) => plot.Left + (logX - x0) / (x1 - x0) * plot.Width;
        double MapY(double logY) => plot.Bottom - (logY - y0) / (y1 - y0) * plot.Height;

        DrawGrid(dc, plot, view, MapX, MapY);

        dc.PushClip(new RectangleGeometry(plot));
        DrawCurve(dc, function, MapX, MapY);
        dc.Pop();

        dc.DrawRectangle(null, AxisPen, plot);
        DrawAxisTitles(dc, plot);

        if (includeReadout && ShowReadout && _dragStart is null && _hover is { } hover && plot.Contains(hover))
            DrawReadout(dc, plot, function, hover, view, MapX, MapY);
    }

    private Rect PlotArea() => new(
        LeftMargin,
        TopMargin,
        Math.Max(0, ActualWidth - LeftMargin - RightMargin),
        Math.Max(0, ActualHeight - TopMargin - BottomMargin));

    private (double X0, double X1, double Y0, double Y1)? CurrentView()
    {
        if (_view is { } view)
            return view;

        if (Function is not { } function || !TryGetDataRange(function, out var range))
            return null;

        _view = range;
        return range;
    }

    private void ZoomAround(Point? anchor, double factor)
    {
        if (CurrentView() is not { } view)
            return;

        var plot = PlotArea();
        var (x0, x1, y0, y1) = view;
        var point = anchor is { } a && plot.Contains(a) ? a : new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);
        var cx = x0 + (point.X - plot.Left) / plot.Width * (x1 - x0);
        var cy = y0 + (plot.Bottom - point.Y) / plot.Height * (y1 - y0);

        var newWidth = Math.Clamp((x1 - x0) * factor, 0.01, 40);
        var newHeight = Math.Clamp((y1 - y0) * factor, 0.01, 40);
        var fx = (cx - x0) / (x1 - x0);
        var fy = (cy - y0) / (y1 - y0);
        _view = (cx - fx * newWidth, cx + (1 - fx) * newWidth, cy - fy * newHeight, cy + (1 - fy) * newHeight);
        InvalidateVisual();
    }

    private void DrawGrid(
        DrawingContext dc, Rect plot, (double X0, double X1, double Y0, double Y1) view,
        Func<double, double> mapX, Func<double, double> mapY)
    {
        var (x0, x1, y0, y1) = view;
        var pixelsPerXDecade = plot.Width / (x1 - x0);
        var pixelsPerYDecade = plot.Height / (y1 - y0);

        if (ShowGrid)
        {
            if (pixelsPerXDecade > 45)
            {
                foreach (var t in MinorTicks(x0, x1))
                    dc.DrawLine(MinorGridPen, new Point(mapX(t), plot.Top), new Point(mapX(t), plot.Bottom));
            }

            if (pixelsPerYDecade > 45)
            {
                foreach (var t in MinorTicks(y0, y1))
                    dc.DrawLine(MinorGridPen, new Point(plot.Left, mapY(t)), new Point(plot.Right, mapY(t)));
            }
        }

        var xStep = DecadeStep(x1 - x0, plot.Width / 58);
        var xTicks = MajorTicks(x0, x1, xStep).ToList();
        foreach (var d in xTicks)
        {
            var x = mapX(d);
            if (ShowGrid)
                dc.DrawLine(MajorGridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Text(FormatDecade(d), 12, TextBrush);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 6));
        }

        var yStep = DecadeStep(y1 - y0, plot.Height / 28);
        var yTicks = MajorTicks(y0, y1, yStep).ToList();
        foreach (var d in yTicks)
        {
            var y = mapY(d);
            if (ShowGrid)
                dc.DrawLine(MajorGridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(FormatDecade(d), 12, TextBrush);
            dc.DrawText(label, new Point(plot.Left - label.Width - 8, y - label.Height / 2));
        }

        // Deep zoom inside one decade: label the minor ticks instead.
        if (xTicks.Count < 2)
        {
            foreach (var t in MinorTicks(x0, x1))
            {
                var label = Text(FormatValue(Math.Pow(10, t)), 11, TextBrush);
                dc.DrawText(label, new Point(mapX(t) - label.Width / 2, plot.Bottom + 6));
            }
        }

        if (yTicks.Count < 2)
        {
            foreach (var t in MinorTicks(y0, y1))
            {
                var label = Text(FormatValue(Math.Pow(10, t)), 11, TextBrush);
                dc.DrawText(label, new Point(plot.Left - label.Width - 8, mapY(t) - label.Height / 2));
            }
        }
    }

    private void DrawCurve(DrawingContext dc, TabulatedFunction function, Func<double, double> mapX, Func<double, double> mapY)
    {
        var geometry = new StreamGeometry();
        var markers = ShowMarkers && function.Count <= MaxMarkers ? new List<Point>(function.Count) : null;

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

                var point = new Point(mapX(Math.Log10(x)), mapY(Math.Log10(y)));
                markers?.Add(point);
                if (!inFigure)
                {
                    ctx.BeginFigure(point, isFilled: false, isClosed: false);
                    inFigure = true;
                }
                else
                {
                    ctx.LineTo(point, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, CurvePen, geometry);

        if (markers is not null)
        {
            foreach (var point in markers)
                dc.DrawEllipse(Brushes.White, CurvePen, point, 2.5, 2.5);
        }
    }

    private void DrawAxisTitles(DrawingContext dc, Rect plot)
    {
        var xTitle = Text(XAxisTitle, 14, TitleBrush);
        dc.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 26));

        var yTitle = Text(YAxisTitle, 14, TitleBrush);
        var center = new Point(16, plot.Top + plot.Height / 2);
        dc.PushTransform(new RotateTransform(-90, center.X, center.Y));
        dc.DrawText(yTitle, new Point(center.X - yTitle.Width / 2, center.Y - yTitle.Height / 2));
        dc.Pop();
    }

    private void DrawReadout(
        DrawingContext dc, Rect plot, TabulatedFunction function, Point hover,
        (double X0, double X1, double Y0, double Y1) view, Func<double, double> mapX, Func<double, double> mapY)
    {
        var (x0, x1, _, _) = view;
        var energy = Math.Pow(10, x0 + (hover.X - plot.Left) / plot.Width * (x1 - x0));
        var sigma = function.Evaluate(energy);

        dc.DrawLine(CursorPen, new Point(hover.X, plot.Top), new Point(hover.X, plot.Bottom));
        if (sigma > 0)
        {
            var y = mapY(Math.Log10(sigma));
            if (y >= plot.Top && y <= plot.Bottom)
                dc.DrawEllipse(CurveBrush, new Pen(Brushes.White, 1.5), new Point(hover.X, y), 4, 4);
        }

        var text = Text($"E = {FormatValue(energy)} eV\nσ = {FormatValue(sigma)} b", 12, Brushes.White);
        var box = new Rect(hover.X + 12, hover.Y + 12, text.Width + 16, text.Height + 10);
        if (box.Right > plot.Right)
            box.X = hover.X - box.Width - 12;
        if (box.Bottom > plot.Bottom)
            box.Y = hover.Y - box.Height - 12;

        dc.DrawRoundedRectangle(ReadoutBackground, null, box, 5, 5);
        dc.DrawText(text, new Point(box.X + 8, box.Y + 5));
    }

    private static bool TryGetDataRange(TabulatedFunction function, out (double X0, double X1, double Y0, double Y1) range)
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
            range = default;
            return false;
        }

        var x0 = Math.Floor(Math.Log10(minX));
        var x1 = Math.Ceiling(Math.Log10(maxX));
        var y0 = Math.Floor(Math.Log10(minY));
        var y1 = Math.Ceiling(Math.Log10(maxY));
        range = (x0, x1 > x0 ? x1 : x0 + 1, y0, y1 > y0 ? y1 : y0 + 1);
        return true;
    }

    private static IEnumerable<double> MajorTicks(double from, double to, double step)
    {
        var first = Math.Ceiling(from / step - 1e-9) * step;
        for (var d = first; d <= to + 1e-9; d += step)
            yield return Math.Round(d, 9);
    }

    private static IEnumerable<double> MinorTicks(double from, double to)
    {
        for (var decade = Math.Floor(from); decade <= to; decade++)
        {
            for (var k = 2; k <= 9; k++)
            {
                var t = decade + Math.Log10(k);
                if (t > from && t < to)
                    yield return t;
            }
        }
    }

    private static double DecadeStep(double decades, double maxLabels) =>
        Math.Max(1, Math.Ceiling(decades / Math.Max(1, Math.Floor(maxLabels))));

    private static string FormatDecade(double exponent) => exponent switch
    {
        0 => "1",
        1 => "10",
        _ => "1E" + exponent.ToString("0", CultureInfo.InvariantCulture),
    };

    private static string FormatValue(double value) =>
        value == 0 ? "0"
        : Math.Abs(value) is >= 1e-3 and < 1e5 ? value.ToString("G4", CultureInfo.InvariantCulture)
        : value.ToString("0.###E+00", CultureInfo.InvariantCulture);

    private void DrawCentered(DrawingContext dc, Rect bounds, string message)
    {
        var text = Text(message, 14, TextBrush);
        dc.DrawText(text, new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
    }

    private FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            size >= 14 ? TitleFont : LabelFont, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
