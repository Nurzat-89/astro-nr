namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// Neutron-capture / decay network for nuclei from the seed up to <c>maxZ</c>.
/// Species are the nuclides reachable from the seed through (n,γ) and decays for which data exists.
/// </summary>
/// <remarks>
/// Captures raise A by one and β decays keep A, so the implicit (backward Euler) system
/// (I − Δt·M) Y(t+Δt) = Y(t) is block lower-triangular in A. Each step is solved exactly by
/// forward substitution over mass numbers, with a small dense solve for the isobars of each A.
/// Flows to nuclides without data (or beyond maxZ) are collected as "lost".
/// </remarks>
internal sealed class CaptureNetwork
{
    private readonly Nuclide[] _species;
    private readonly Dictionary<Nuclide, int> _index;
    private readonly double[] _sigmaCm2;
    private readonly double[] _lambda;
    private readonly double[] _halfLife;
    private readonly int[] _captureTarget;
    private readonly int[] _captureParent;
    private readonly (int Target, double Fraction, int DaughterA)[][] _decayTargets;
    private readonly (int Start, int Count)[] _blocks;

    public CaptureNetwork(SProcessNuclearData data, Nuclide seed, int maxZ, double kTkeV)
    {
        // Breadth-first search from the seed over captures and decays.
        var reachable = new SortedSet<Nuclide>();
        var queue = new Queue<Nuclide>();
        if (HasData(data, seed))
        {
            reachable.Add(seed);
            queue.Enqueue(seed);
        }

        while (queue.Count > 0)
        {
            var nuclide = queue.Dequeue();
            foreach (var next in Successors(data, nuclide, kTkeV))
            {
                if (next.Z >= seed.Z && next.Z <= maxZ && HasData(data, next) && reachable.Add(next))
                    queue.Enqueue(next);
            }
        }

        _species = reachable.ToArray(); // ordered by A, then Z
        _index = _species.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        var count = _species.Length;

        _sigmaCm2 = new double[count];
        _lambda = new double[count];
        _halfLife = new double[count];
        _captureTarget = new int[count];
        _captureParent = new int[count];
        _decayTargets = new (int, double, int)[count][];

        for (var i = 0; i < count; i++)
        {
            var nuclide = _species[i];
            _sigmaCm2[i] = data.CaptureBarns(nuclide, kTkeV) * 1e-24;
            var decay = data.Decays.GetValueOrDefault(nuclide) ?? RadioactiveDecay.Stable;
            _lambda[i] = decay.DecayConstant;
            _halfLife[i] = decay.HalfLifeSeconds;
            _captureTarget[i] = _index.GetValueOrDefault(nuclide with { A = nuclide.A + 1 }, -1);
            _captureParent[i] = _index.GetValueOrDefault(nuclide with { A = nuclide.A - 1 }, -1);

            var total = decay.Branches.Where(b => b.Fraction > 0).Sum(b => b.Fraction);
            _decayTargets[i] = decay.IsStable || total <= 0
                ? []
                : decay.Branches
                    .Where(b => b.Fraction > 0)
                    .Select(b =>
                    {
                        var daughter = new Nuclide(nuclide.Z + b.DeltaZ, nuclide.A + b.DeltaA);
                        var target = b.IsFission ? -1 : _index.GetValueOrDefault(daughter, -1);
                        return (target, b.Fraction / total, daughter.A);
                    })
                    .ToArray();
        }

        var blocks = new List<(int, int)>();
        for (var i = 0; i < count;)
        {
            var start = i;
            while (i < count && _species[i].A == _species[start].A)
                i++;
            blocks.Add((start, i - start));
        }

        _blocks = blocks.ToArray();
    }

    public IReadOnlyList<Nuclide> Species => _species;

    public int Count => _species.Length;

    public int IndexOf(Nuclide nuclide) => _index.GetValueOrDefault(nuclide, -1);

    public double SigmaCm2(int i) => _sigmaCm2[i];

    public double HalfLife(int i) => _halfLife[i];

    /// <summary>Σ Yᵢ σᵢ over the network (mol/g · cm²).</summary>
    public double AbsorptionSum(double[] y)
    {
        var sum = 0.0;
        for (var i = 0; i < y.Length; i++)
            sum += y[i] * _sigmaCm2[i];
        return sum;
    }

    public double MaxSigmaCm2 => _sigmaCm2.Length == 0 ? 0 : _sigmaCm2.Max();

    /// <summary>
    /// Advances the abundances <paramref name="y"/> (mol/g) by <paramref name="dt"/> seconds with neutron flux
    /// n_n·v_T = <paramref name="flux"/> (cm⁻² s⁻¹), using backward Euler.
    /// </summary>
    /// <returns>Moles per gram and mass fraction (Σ Y·A) that left the network during the step.</returns>
    public (double LostMoles, double LostMass) Step(double[] y, double flux, double dt)
    {
        var lostMoles = 0.0;
        var lostMass = 0.0;
        var previous = (double[])y.Clone();

        foreach (var (start, size) in _blocks)
        {
            var a = new double[size, size];
            var b = new double[size];

            for (var r = 0; r < size; r++)
            {
                var i = start + r;
                a[r, r] = 1.0 + dt * (flux * _sigmaCm2[i] + _lambda[i]);
                b[r] = previous[i];
            }

            // Captures from the block of A-1 (already solved) feed this block.
            for (var r = 0; r < size; r++)
            {
                var parent = _captureParent[start + r];
                if (parent >= 0)
                    b[r] += dt * flux * _sigmaCm2[parent] * y[parent];
            }

            // Decays inside the block (β-, β+/EC keep A) couple the isobars.
            for (var c = 0; c < size; c++)
            {
                var j = start + c;
                foreach (var (target, fraction, _) in _decayTargets[j])
                {
                    if (target >= start && target < start + size)
                        a[target - start, c] -= dt * _lambda[j] * fraction;
                }
            }

            // Decays from heavier blocks that lower A (β-n, α) are rare in the s-process and are fed explicitly.
            Solve(a, b, size);
            for (var r = 0; r < size; r++)
                y[start + r] = Math.Max(0.0, b[r]);
        }

        // Explicit bookkeeping of flows that leave the network or lower A.
        for (var i = 0; i < y.Length; i++)
        {
            if (y[i] <= 0)
                continue;

            if (_captureTarget[i] < 0 && _sigmaCm2[i] > 0)
            {
                var flow = dt * flux * _sigmaCm2[i] * y[i];
                lostMoles += flow;
                lostMass += flow * (_species[i].A + 1);
            }

            foreach (var (target, fraction, daughterA) in _decayTargets[i])
            {
                var flow = dt * _lambda[i] * fraction * y[i];
                if (target < 0)
                {
                    lostMoles += flow;
                    lostMass += flow * daughterA;
                }
                else if (_species[target].A < _species[i].A)
                {
                    y[target] += flow;
                }
            }
        }

        return (lostMoles, lostMass);
    }

    /// <summary>
    /// Lets every nuclide with T½ below <paramref name="halfLifeCutoffSeconds"/> decay completely.
    /// </summary>
    /// <returns>Mass fraction that decayed out of the network.</returns>
    public double DecayShortLived(double[] y, double halfLifeCutoffSeconds)
    {
        var lostMass = 0.0;
        for (var pass = 0; pass < 100; pass++)
        {
            var moved = false;
            for (var i = 0; i < y.Length; i++)
            {
                if (y[i] <= 0 || _lambda[i] <= 0 || _halfLife[i] >= halfLifeCutoffSeconds || _decayTargets[i].Length == 0)
                    continue;

                var amount = y[i];
                y[i] = 0;
                moved = true;
                foreach (var (target, fraction, daughterA) in _decayTargets[i])
                {
                    if (target >= 0)
                        y[target] += amount * fraction;
                    else
                        lostMass += amount * fraction * daughterA;
                }
            }

            if (!moved)
                break;
        }

        return lostMass;
    }

    private static bool HasData(SProcessNuclearData data, Nuclide nuclide) =>
        data.CaptureCrossSections.ContainsKey(nuclide) || data.Decays.ContainsKey(nuclide);

    private static IEnumerable<Nuclide> Successors(SProcessNuclearData data, Nuclide nuclide, double kTkeV)
    {
        if (data.CaptureBarns(nuclide, kTkeV) > 0)
            yield return nuclide with { A = nuclide.A + 1 };

        if (data.Decays.TryGetValue(nuclide, out var decay) && !decay.IsStable)
        {
            foreach (var branch in decay.Branches.Where(b => b.Fraction > 0 && !b.IsFission))
                yield return new Nuclide(nuclide.Z + branch.DeltaZ, nuclide.A + branch.DeltaA);
        }
    }

    /// <summary>Gaussian elimination with partial pivoting; the solution replaces <paramref name="b"/>.</summary>
    private static void Solve(double[,] a, double[] b, int n)
    {
        if (n == 1)
        {
            b[0] /= a[0, 0];
            return;
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    pivot = r;
            }

            if (pivot != col)
            {
                for (var c = 0; c < n; c++)
                    (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }

            for (var r = col + 1; r < n; r++)
            {
                var factor = a[r, col] / a[col, col];
                if (factor == 0)
                    continue;
                for (var c = col; c < n; c++)
                    a[r, c] -= factor * a[col, c];
                b[r] -= factor * b[col];
            }
        }

        for (var r = n - 1; r >= 0; r--)
        {
            var sum = b[r];
            for (var c = r + 1; c < n; c++)
                sum -= a[r, c] * b[c];
            b[r] = sum / a[r, r];
        }
    }
}
