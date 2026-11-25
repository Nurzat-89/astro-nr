namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// A one-dimensional tabulated function y(x) with ENDF interpolation regions (TAB1 record).
/// </summary>
public sealed class TabulatedFunction
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly InterpolationRegion[] _regions;

    public TabulatedFunction(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<InterpolationRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(regions);

        if (x.Count != y.Count)
            throw new ArgumentException($"X has {x.Count} points but Y has {y.Count}.", nameof(y));
        if (x.Count == 0)
            throw new ArgumentException("A tabulated function needs at least one point.", nameof(x));
        if (regions.Count == 0)
            throw new ArgumentException("A tabulated function needs at least one interpolation region.", nameof(regions));

        for (var i = 1; i < x.Count; i++)
        {
            if (x[i] < x[i - 1])
                throw new ArgumentException($"X values must be non-decreasing (point {i + 1}: {x[i]} < {x[i - 1]}).", nameof(x));
        }

        _x = [.. x];
        _y = [.. y];
        _regions = [.. regions];
    }

    public IReadOnlyList<double> X => _x;

    public IReadOnlyList<double> Y => _y;

    public IReadOnlyList<InterpolationRegion> Regions => _regions;

    public int Count => _x.Length;

    public double MinX => _x[0];

    public double MaxX => _x[^1];

    /// <summary>
    /// Evaluates the function at <paramref name="x"/> using the ENDF interpolation laws.
    /// Returns 0 outside the tabulated range. At a discontinuity (repeated x) the right-hand value is returned.
    /// </summary>
    public double Evaluate(double x)
    {
        if (double.IsNaN(x) || x < _x[0] || x > _x[^1])
            return 0.0;

        var index = Array.BinarySearch(_x, x);
        if (index >= 0)
        {
            while (index + 1 < _x.Length && _x[index + 1] == x)
                index++;
            return _y[index];
        }

        var i = ~index - 1; // _x[i] < x < _x[i + 1]
        return Interpolate(LawForInterval(i), _x[i], _y[i], _x[i + 1], _y[i + 1], x);
    }

    /// <summary>Interpolation law of the interval between 0-based points <paramref name="i"/> and i+1.</summary>
    private InterpolationLaw LawForInterval(int i)
    {
        var upperPoint = i + 2; // 1-based index of the interval's upper point
        foreach (var region in _regions)
        {
            if (upperPoint <= region.LastPoint)
                return region.Law;
        }

        return _regions[^1].Law;
    }

    private static double Interpolate(InterpolationLaw law, double x1, double y1, double x2, double y2, double x)
    {
        switch (law)
        {
            case InterpolationLaw.Histogram:
                return y1;

            case InterpolationLaw.LinLog when x1 > 0 && x2 > 0 && x > 0:
                return y1 + (y2 - y1) * Math.Log(x / x1) / Math.Log(x2 / x1);

            case InterpolationLaw.LogLin when y1 > 0 && y2 > 0:
                return y1 * Math.Pow(y2 / y1, (x - x1) / (x2 - x1));

            case InterpolationLaw.LogLog when x1 > 0 && x2 > 0 && x > 0 && y1 > 0 && y2 > 0:
                return y1 * Math.Pow(y2 / y1, Math.Log(x / x1) / Math.Log(x2 / x1));

            default:
                // LinLin, ChargedParticle, and log laws that cannot be applied to non-positive values.
                return y1 + (y2 - y1) * (x - x1) / (x2 - x1);
        }
    }
}
