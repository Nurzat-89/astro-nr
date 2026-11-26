namespace AstroNucleaus.Domain.RProcess;

/// <summary>
/// Classical r-process with a full reaction network at constant temperature and neutron density.
/// </summary>
/// <remarks>
/// <para>Starting from a seed (⁵⁶Fe), every nucleus captures neutrons at λ(n,γ) = n_n·N_A⟨σv⟩/N_A
/// (REACLIB), is photodisintegrated at λ(γ,n) and β⁻-decays (with delayed neutrons) at the REACLIB weak rates.
/// λ(γ,n) follows from detailed balance with the neutron separation energy S_n from the masses:</para>
/// <code>λ(γ,n)(Z,A) = 9.8686e9 · T9^{3/2} · (2·g(Z,A−1)/g(Z,A)) · ((A−1)/A)^{3/2} · exp(−11.6045·S_n/T9) · N_A⟨σv⟩(n,γ)(Z,A−1)</code>
/// <para>with g = 2J+1 of the ground states (partition functions ≈ ground-state weight). Where a mass is
/// missing, the REACLIB reverse rate is used. After the irradiation the neutron reactions stop at once
/// (freeze-out) and the products β-decay for the decay time.</para>
/// <para>Not modelled: fission (matter decaying beyond MaxZ is counted as lost), α decay, β⁺/EC, charged-particle
/// reactions, neutrons released by delayed emission and the change of T and n_n during the expansion.</para>
/// </remarks>
public sealed class RProcessCalculator
{
    public const double SecondsPerYear = 3.15576e7;
    private const double KeVPerKelvin = 8.617333262e-8;
    private const int DecaySteps = 400;
    private const double FirstDecayStepSeconds = 1e-3;

    private readonly RProcessNuclearData _data;

    public RProcessCalculator(RProcessNuclearData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public RProcessResult Run(RProcessParameters parameters, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var errors = parameters.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(parameters));
        if (!_data.NeutronCapture.ContainsKey(parameters.Seed))
            throw new InvalidOperationException($"The REACLIB data has no (n,γ) rate for the seed {parameters.Seed}.");

        var network = new IsotopeChainNetwork(_data, parameters);
        var y = new double[network.Count];
        var seedIndex = network.IndexOf(parameters.Seed);
        y[seedIndex] = 1.0;

        var history = new List<RProcessSnapshot> { Snapshot(0) };
        var frames = new List<double[]>();
        var frameInfos = new List<RProcessFrameInfo>();
        RecordFrame(0, irradiation: true);
        var lost = 0.0;
        var dt = parameters.DurationSeconds / parameters.TimeSteps;
        var recordEvery = Math.Max(1, parameters.TimeSteps / 200);

        for (var step = 1; step <= parameters.TimeSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lost += network.Step(y, dt, irradiate: true);

            if (step % recordEvery == 0 || step == parameters.TimeSteps)
            {
                history.Add(Snapshot(step * dt));
                RecordFrame(step * dt, irradiation: true);
            }
            if (step % Math.Max(1, parameters.TimeSteps / 100) == 0)
                progress?.Report(0.9 * step / parameters.TimeSteps);
        }

        var atFreezeOut = (double[])y.Clone();

        // Freeze-out: neutron reactions stop, β decays continue on a logarithmic time grid.
        var decaySeconds = parameters.DecayYears * SecondsPerYear;
        if (decaySeconds > 0)
        {
            var previous = 0.0;
            var ratio = Math.Pow(Math.Max(decaySeconds / FirstDecayStepSeconds, 1.0), 1.0 / DecaySteps);
            var time = FirstDecayStepSeconds;
            for (var step = 0; step < DecaySteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = Math.Min(time, decaySeconds);
                lost += network.Step(y, next - previous, irradiate: false);
                previous = next;
                time *= ratio;
                if (step % 4 == 3)
                    RecordFrame(parameters.DurationSeconds + previous, irradiation: false);
            }

            if (previous < decaySeconds)
                lost += network.Step(y, decaySeconds - previous, irradiate: false);
            RecordFrame(parameters.DurationSeconds + decaySeconds, irradiation: false);
        }

        progress?.Report(1.0);

        // Keep every nucleus that is ever present, also those the path only passes through.
        var everPresent = new bool[network.Count];
        foreach (var frame in frames)
        {
            for (var i = 0; i < frame.Length; i++)
                everPresent[i] |= frame[i] >= 1e-30;
        }

        var abundances = new List<RProcessAbundance>();
        var kept = new List<int>();
        for (var i = 0; i < network.Count; i++)
        {
            if (atFreezeOut[i] < 1e-30 && y[i] < 1e-30 && !everPresent[i])
                continue;
            kept.Add(i);
            var beta = network.BetaRate(i);
            abundances.Add(new RProcessAbundance(
                network.Species[i],
                atFreezeOut[i],
                y[i],
                beta > 0 ? Math.Log(2) / beta : double.PositiveInfinity,
                _data.NeutronSeparationEnergy(network.Species[i])));
        }

        var chains = abundances
            .GroupBy(a => a.Nuclide.A)
            .Select(g => new MassChainAbundance(g.Key, g.Sum(a => a.AtFreezeOut), g.Sum(a => a.Decayed)))
            .OrderBy(c => c.A)
            .ToList();

        var waitingPoints = abundances
            .GroupBy(a => a.Nuclide.Z)
            .Where(g => g.Sum(a => a.AtFreezeOut) > 1e-4)
            .Select(g => g.MaxBy(a => a.AtFreezeOut)!.Nuclide)
            .OrderBy(n => n.Z)
            .ToList();

        var meanFreeze = MeanA(atFreezeOut);
        var warnings = new List<string>();
        if (network.PhotodisintegrationFromReaclib > 0)
            warnings.Add($"{network.PhotodisintegrationFromReaclib} nuclides have no mass: their (γ,n) rate is the REACLIB reverse rate.");
        if (network.PhotodisintegrationMissing > 0)
            warnings.Add($"{network.PhotodisintegrationMissing} nuclides have neither masses nor a REACLIB reverse rate: no (γ,n) for them.");
        if (lost > 0.01)
            warnings.Add($"{lost:P1} of the matter decayed beyond Z = {parameters.MaxZ}; fission is not modelled.");

        // Unknown nuclei (no mass) without β-decay data act as artificial stable sinks.
        var stuck = abundances
            .Where(a => double.IsPositiveInfinity(a.BetaHalfLifeSeconds) && !_data.MassExcessMeV.ContainsKey(a.Nuclide) && a.Decayed > 0)
            .ToList();
        var stuckTotal = stuck.Sum(a => a.Decayed);
        if (stuckTotal > 0.01)
        {
            var examples = string.Join(", ", stuck.OrderByDescending(a => a.Decayed).Take(3).Select(a => a.Nuclide));
            warnings.Add($"{stuckTotal:P1} of the matter ends in unknown nuclei without β-decay data ({examples}); they act as stable.");
        }

        return new RProcessResult(
            parameters,
            KeVPerKelvin * parameters.TemperatureGK * 1e9,
            network.Count,
            network.PhotodisintegrationFromMasses,
            network.PhotodisintegrationFromReaclib,
            meanFreeze - parameters.Seed.A,
            meanFreeze,
            MeanA(y),
            lost,
            history,
            abundances,
            chains,
            waitingPoints,
            warnings,
            _data.Description)
        {
            FrameInfos = frameInfos,
            FrameAbundances = frames.Select(f => kept.Select(i => f[i]).ToArray()).ToList(),
        };

        void RecordFrame(double timeSeconds, bool irradiation)
        {
            var snapshot = Snapshot(timeSeconds);
            frames.Add((double[])y.Clone());
            frameInfos.Add(new RProcessFrameInfo(timeSeconds, irradiation, snapshot.MeanZ, snapshot.MeanA));
        }

        double MeanA(double[] values)
        {
            double sum = 0, weighted = 0;
            for (var i = 0; i < values.Length; i++)
            {
                sum += values[i];
                weighted += values[i] * network.Species[i].A;
            }

            return sum > 0 ? weighted / sum : 0;
        }

        RProcessSnapshot Snapshot(double time)
        {
            double sum = 0, z = 0, a = 0;
            for (var i = 0; i < y.Length; i++)
            {
                sum += y[i];
                z += y[i] * network.Species[i].Z;
                a += y[i] * network.Species[i].A;
            }

            return sum > 0
                ? new RProcessSnapshot(time, z / sum, a / sum, y[seedIndex])
                : new RProcessSnapshot(time, 0, 0, 0);
        }
    }
}
