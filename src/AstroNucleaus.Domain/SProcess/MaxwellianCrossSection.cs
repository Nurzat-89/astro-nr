namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// Maxwellian-averaged (n,γ) cross section (MACS) tabulated against thermal energy kT.
/// Values between grid points are interpolated log-log; outside the grid the edge value is used.
/// </summary>
public sealed class MaxwellianCrossSection
{
    private readonly double[] _kT;
    private readonly double[] _sigma;

    /// <param name="points">(kT in keV, MACS in barns) pairs in any order.</param>
    public MaxwellianCrossSection(IEnumerable<(double KTkeV, double Barns)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var sorted = points
            .Where(p => p.KTkeV > 0 && double.IsFinite(p.Barns) && p.Barns >= 0)
            .GroupBy(p => p.KTkeV)
            .Select(g => g.First())
            .OrderBy(p => p.KTkeV)
            .ToArray();

        if (sorted.Length == 0)
            throw new ArgumentException("At least one (kT, MACS) point with kT > 0 is required.", nameof(points));

        _kT = sorted.Select(p => p.KTkeV).ToArray();
        _sigma = sorted.Select(p => p.Barns).ToArray();
    }

    public IReadOnlyList<double> KTkeV => _kT;

    public IReadOnlyList<double> Barns => _sigma;

    /// <summary>MACS in barns at thermal energy <paramref name="kTkeV"/> (keV).</summary>
    public double Evaluate(double kTkeV)
    {
        if (kTkeV <= _kT[0])
            return _sigma[0];
        if (kTkeV >= _kT[^1])
            return _sigma[^1];

        var index = Array.BinarySearch(_kT, kTkeV);
        if (index >= 0)
            return _sigma[index];

        var i = ~index - 1;
        double x1 = _kT[i], x2 = _kT[i + 1], y1 = _sigma[i], y2 = _sigma[i + 1];
        if (y1 > 0 && y2 > 0)
            return y1 * Math.Pow(y2 / y1, Math.Log(kTkeV / x1) / Math.Log(x2 / x1));

        return y1 + (y2 - y1) * (kTkeV - x1) / (x2 - x1);
    }
}
