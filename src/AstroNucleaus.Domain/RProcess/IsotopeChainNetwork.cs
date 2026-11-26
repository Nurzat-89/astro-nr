using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Domain.RProcess;

/// <summary>
/// r-process network ordered by element: each element is an isotopic chain coupled by (n,γ) and (γ,n);
/// β⁻ decays (with delayed neutrons) move matter to Z+1.
/// </summary>
/// <remarks>
/// The backward Euler system (I − Δt·M)·Y(t+Δt) = Y(t) is block lower-triangular in Z, and each block
/// (one element) is tridiagonal in A. One step is solved exactly by going through the elements in order of Z
/// with the Thomas algorithm. β flows beyond <c>maxZ</c> are counted as lost.
/// </remarks>
internal sealed class IsotopeChainNetwork
{
    private const double Avogadro = 6.02214076e23;

    /// <summary>(m_u·k·T / 2πħ²)^{3/2} / N_A at T9 = 1, in mol cm⁻³ (detailed balance for (γ,n)).</summary>
    private const double SahaConstant = 9.8686e9;

    /// <summary>1 MeV / (k · 1 GK).</summary>
    private const double MeVPerKT9 = 11.6045;

    private readonly Nuclide[] _species;
    private readonly Dictionary<Nuclide, int> _index;
    private readonly (int Start, int Count)[] _chains;
    private readonly double[] _capture;       // 1/s at the given neutron density, to A+1
    private readonly double[] _photo;         // 1/s, to A−1
    private readonly double[] _beta;          // total β⁻ rate, 1/s
    private readonly (int Target, double Rate)[][] _betaOut;
    private readonly (int Source, double Rate)[][] _betaIn;

    public IsotopeChainNetwork(RProcessNuclearData data, RProcessParameters parameters)
    {
        var t9 = parameters.TemperatureGK;
        var minZ = parameters.Seed.Z;
        var maxZ = parameters.MaxZ;
        bool InRange(Nuclide n) => n.Z >= minZ && n.Z <= maxZ && n.A > n.Z;

        var set = new HashSet<Nuclide> { parameters.Seed };
        foreach (var target in data.NeutronCapture.Keys.Where(InRange))
        {
            set.Add(target);
            set.Add(target with { A = target.A + 1 });
        }

        foreach (var (parent, channels) in data.BetaDecays)
        {
            if (!InRange(parent))
                continue;
            set.Add(parent);
            foreach (var channel in channels.Where(c => InRange(c.Daughter)))
                set.Add(channel.Daughter);
        }

        _species = set.OrderBy(n => n.Z).ThenBy(n => n.A).ToArray();
        _index = _species.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        var count = _species.Length;

        _chains = _species
            .Select((n, i) => (n.Z, i))
            .GroupBy(p => p.Z)
            .Select(g => (g.First().i, g.Count()))
            .ToArray();

        _capture = new double[count];
        _photo = new double[count];
        _beta = new double[count];
        _betaOut = new (int, double)[count][];
        var betaIn = Enumerable.Range(0, count).Select(_ => new List<(int, double)>()).ToArray();
        var captureNA = new double[count];

        for (var i = 0; i < count; i++)
        {
            var nuclide = _species[i];
            if (_index.ContainsKey(nuclide with { A = nuclide.A + 1 }) && data.NeutronCapture.TryGetValue(nuclide, out var capture))
            {
                captureNA[i] = capture.Evaluate(t9);
                _capture[i] = parameters.NeutronDensity * captureNA[i] / Avogadro;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var nuclide = _species[i];
            var lighter = nuclide with { A = nuclide.A - 1 };
            if (_index.TryGetValue(lighter, out var j) && captureNA[j] > 0)
            {
                if (data.NeutronSeparationEnergy(nuclide) is { } sn)
                {
                    var exponent = Math.Min(700, -MeVPerKT9 * sn / t9);
                    var weights = 2.0 * data.StatisticalWeight(lighter) / data.StatisticalWeight(nuclide);
                    _photo[i] = SahaConstant * Math.Pow(t9, 1.5) * weights
                                * Math.Pow((double)lighter.A / nuclide.A, 1.5) * Math.Exp(exponent) * captureNA[j];
                    PhotodisintegrationFromMasses++;
                }
                else if (data.ReversePhotodisintegration.TryGetValue(nuclide, out var reverse))
                {
                    _photo[i] = reverse.Evaluate(t9);
                    PhotodisintegrationFromReaclib++;
                }
                else
                {
                    PhotodisintegrationMissing++;
                }
            }

            var channels = data.BetaDecays.GetValueOrDefault(nuclide) ?? [];
            _beta[i] = channels.Sum(c => c.RatePerSecond);
            _betaOut[i] = channels
                .Where(c => c.RatePerSecond > 0)
                .Select(c => (_index.GetValueOrDefault(c.Daughter, -1), c.RatePerSecond))
                .ToArray();
            foreach (var (target, rate) in _betaOut[i])
            {
                if (target >= 0)
                    betaIn[target].Add((i, rate));
            }
        }

        _betaIn = betaIn.Select(l => l.ToArray()).ToArray();
    }

    public IReadOnlyList<Nuclide> Species => _species;

    public int Count => _species.Length;

    public int PhotodisintegrationFromMasses { get; }

    public int PhotodisintegrationFromReaclib { get; }

    public int PhotodisintegrationMissing { get; }

    public int IndexOf(Nuclide nuclide) => _index.GetValueOrDefault(nuclide, -1);

    public double BetaRate(int i) => _beta[i];

    public double CaptureRate(int i) => _capture[i];

    public double PhotodisintegrationRate(int i) => _photo[i];

    /// <summary>Advances Y by <paramref name="dt"/> seconds; neutron reactions only when <paramref name="irradiate"/>.</summary>
    /// <returns>Abundance (Y/Y₀) that decayed beyond the network.</returns>
    public double Step(double[] y, double dt, bool irradiate)
    {
        var lost = 0.0;
        foreach (var (start, size) in _chains)
        {
            var lower = new double[size];
            var diagonal = new double[size];
            var upper = new double[size];
            var rhs = new double[size];

            for (var k = 0; k < size; k++)
            {
                var i = start + k;
                var capture = irradiate ? _capture[i] : 0;
                var photo = irradiate ? _photo[i] : 0;
                diagonal[k] = 1 + dt * (capture + photo + _beta[i]);
                if (k > 0 && irradiate)
                    lower[k] = -dt * _capture[i - 1];
                if (k < size - 1 && irradiate)
                    upper[k] = -dt * _photo[i + 1];

                var inflow = 0.0;
                foreach (var (source, rate) in _betaIn[i])
                    inflow += rate * y[source]; // sources have lower Z: already advanced
                rhs[k] = y[i] + dt * inflow;
            }

            SolveTridiagonal(lower, diagonal, upper, rhs);
            for (var k = 0; k < size; k++)
            {
                var i = start + k;
                y[i] = Math.Max(0, rhs[k]);
                foreach (var (target, rate) in _betaOut[i])
                {
                    if (target < 0)
                        lost += dt * rate * y[i];
                }
            }
        }

        return lost;
    }

    /// <summary>Thomas algorithm; the solution replaces <paramref name="d"/>.</summary>
    private static void SolveTridiagonal(double[] a, double[] b, double[] c, double[] d)
    {
        var n = d.Length;
        for (var k = 1; k < n; k++)
        {
            var w = a[k] / b[k - 1];
            b[k] -= w * c[k - 1];
            d[k] -= w * d[k - 1];
        }

        d[n - 1] /= b[n - 1];
        for (var k = n - 2; k >= 0; k--)
            d[k] = (d[k] - c[k] * d[k + 1]) / b[k];
    }
}
