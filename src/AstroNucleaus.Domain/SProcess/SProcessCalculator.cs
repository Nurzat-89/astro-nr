namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// One-zone weak s-process in the convective He-burning core of a massive star.
/// </summary>
/// <remarks>
/// <para>²²Ne(α,n) only becomes efficient at the higher temperatures reached near the end of core He burning,
/// so the run covers the final phase, from a central He mass fraction of <see cref="SProcessParameters.HeliumAtOnset"/>
/// (default 0.1) down to zero, at the given average temperature. Helium is consumed linearly in time at the
/// average rate of the whole He-burning time; burnt helium becomes ¹²C and ¹⁶O, and the ash of the earlier
/// burning is present from the start. ²²Ne is destroyed by ²²Ne(α,n)²⁵Mg and ²²Ne(α,γ)²⁶Mg
/// (REACLIB, Longland et al. 2012); burning of ²²Ne before the onset is neglected.</para>
/// <para>Neutrons live for much less than a time step, so the neutron density follows from the balance of
/// production and capture: n_n = ρ·Y(α)·Y(²²Ne)·N_A⟨σv⟩(α,n) / (v_T·Σ Yᵢ·σᵢ), with σᵢ the Maxwellian-averaged
/// capture cross sections at kT of all network nuclei and the light poisons (¹²C, ¹⁶O, ²⁰,²²Ne, ²⁴⁻²⁶Mg).
/// Each nucleus then captures at the rate n_n·v_T·σᵢ.</para>
/// <para>Simplifications: terrestrial half-lives (no temperature dependence), ground states only,
/// neutrons captured by ¹⁶O are not recycled through ¹⁷O(α,n), and no convective mixing with other zones.</para>
/// </remarks>
public sealed class SProcessCalculator
{
    private const double Boltzmann = 1.380649e-16;      // erg/K
    private const double NeutronMass = 1.67492750e-24;  // g
    private const double KeVPerKelvin = 8.617333262e-8; // keV/K
    private const double CmMinus2PerInverseMillibarn = 1e27;

    /// <summary>Largest neutron exposure (mb⁻¹) in one time step; keeps σ·Δτ ≤ 0.1 for σ ≈ 500 mb.</summary>
    private const double MaxExposurePerStep = 2e-4;

    /// <summary>Nuclides with shorter half-lives are decayed for the "decayed" abundances.</summary>
    public const double DecayCutoffYears = 1e6;

    private readonly SProcessNuclearData _data;

    public SProcessCalculator(SProcessNuclearData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public SProcessResult Run(SProcessParameters parameters, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var errors = parameters.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(parameters));

        var conditions = GetConditions(parameters);
        var seed = StellarCoreModel.Fe56;
        var network = new CaptureNetwork(_data, seed, parameters.MaxZ, conditions.KTkeV);
        var seedIndex = network.IndexOf(seed);
        if (seedIndex < 0)
            throw new InvalidOperationException($"The nuclear data has no entry for {seed}.");

        // Initial composition (Y = X / A, mol/g).
        var initial = StellarCoreModel.InitialMassFractions(parameters.Metallicity);
        var y = new double[network.Count];
        y[seedIndex] = initial[seed] / seed.A;
        var initialFe = y[seedIndex];
        var initialY = (double[])y.Clone();

        var poisons = StellarCoreModel.Poisons.ToDictionary(p => p, p => initial.GetValueOrDefault(p) / p.A);
        var poisonSigma = StellarCoreModel.Poisons.ToDictionary(p => p, p => _data.CaptureBarns(p, conditions.KTkeV) * 1e-24);
        var initialNe22 = poisons[StellarCoreModel.Ne22];

        var heliumTotal = StellarCoreModel.InitialHelium(parameters.Metallicity);
        var heliumStart = Math.Min(parameters.HeliumAtOnset, heliumTotal);
        var duration = conditions.PhaseDurationYears * StellarCoreModel.SecondsPerYear;
        SetAsh(heliumTotal - heliumStart);
        var baseStep = duration / parameters.TimeSteps;
        var alphaRateSum = conditions.Ne22AlphaNRate + conditions.Ne22AlphaGammaRate;
        var neutronBranch = alphaRateSum > 0 ? conditions.Ne22AlphaNRate / alphaRateSum : 0;

        double time = 0, exposureCm2 = 0, peakDensity = 0, densityTimeIntegral = 0, lostMass = 0;
        var steps = 0;
        var history = new List<SProcessSnapshot> { new(0, heliumStart, initialNe22 * 22, 0, 0) };
        var nextRecord = duration / 400;
        var nextProgress = 0.0;

        while (time < duration * (1 - 1e-12))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dt = Math.Min(baseStep, duration - time);
            var neutronDensity = NeutronDensity(dt);

            // Limit the neutron exposure per step for accuracy.
            var exposurePerStep = neutronDensity * conditions.ThermalVelocity * dt / CmMinus2PerInverseMillibarn;
            if (exposurePerStep > MaxExposurePerStep)
            {
                dt *= MaxExposurePerStep / exposurePerStep;
                neutronDensity = NeutronDensity(dt);
            }

            // ²²Ne burning over the step (exact for constant Y(α)).
            var heliumMid = Helium(time + dt / 2);
            var ne22 = poisons[StellarCoreModel.Ne22];
            var burned = ne22 * -double.ExpM1(-conditions.Density * heliumMid / 4 * alphaRateSum * dt);
            poisons[StellarCoreModel.Ne22] = ne22 - burned;
            poisons[StellarCoreModel.Mg25] += burned * neutronBranch;
            poisons[StellarCoreModel.Mg26] += burned * (1 - neutronBranch);

            var flux = neutronDensity * conditions.ThermalVelocity;
            lostMass += network.Step(y, flux, dt).LostMass;

            time += dt;
            steps++;
            exposureCm2 += flux * dt;
            densityTimeIntegral += neutronDensity * dt;
            peakDensity = Math.Max(peakDensity, neutronDensity);

            SetAsh(heliumTotal - Helium(time));

            if (time >= nextRecord || time >= duration * (1 - 1e-12))
            {
                history.Add(new SProcessSnapshot(
                    time / StellarCoreModel.SecondsPerYear,
                    Helium(time),
                    poisons[StellarCoreModel.Ne22] * 22,
                    neutronDensity,
                    exposureCm2 / CmMinus2PerInverseMillibarn));
                nextRecord = time + duration / 400;
            }

            if (progress is not null && time / duration >= nextProgress)
            {
                progress.Report(time / duration);
                nextProgress += 0.01;
            }

            double NeutronDensity(double step)
            {
                var helium = Helium(time + step / 2);
                var ne = poisons[StellarCoreModel.Ne22];
                var burnedInStep = ne * -double.ExpM1(-conditions.Density * helium / 4 * alphaRateSum * step);
                var production = burnedInStep * neutronBranch / step; // mol g⁻¹ s⁻¹
                var absorption = network.AbsorptionSum(y);
                foreach (var poison in StellarCoreModel.Poisons)
                    absorption += poisons[poison] * poisonSigma[poison];
                return absorption > 0 ? production / (conditions.ThermalVelocity * absorption) : 0;
            }
        }

        progress?.Report(1.0);

        var decayed = (double[])y.Clone();
        network.DecayShortLived(decayed, DecayCutoffYears * StellarCoreModel.SecondsPerYear);

        var abundances = new List<NuclideAbundance>(network.Count);
        for (var i = 0; i < network.Count; i++)
        {
            var nuclide = network.Species[i];
            abundances.Add(new NuclideAbundance(
                nuclide,
                initialY[i] * nuclide.A,
                y[i] * nuclide.A,
                decayed[i] * nuclide.A,
                y[i] / initialFe,
                decayed[i] / initialFe,
                network.HalfLife(i),
                network.SigmaCm2(i) * 1e24));
        }

        return new SProcessResult(
            parameters,
            conditions,
            exposureCm2 / CmMinus2PerInverseMillibarn,
            peakDensity,
            densityTimeIntegral / duration,
            1 - poisons[StellarCoreModel.Ne22] / initialNe22,
            1 - y[seedIndex] / initialFe,
            lostMass,
            steps,
            history,
            abundances,
            _data.Description);

        double Helium(double t) => Math.Max(0.0, heliumStart * (1 - t / duration));

        void SetAsh(double burntHelium)
        {
            poisons[StellarCoreModel.C12] = parameters.CarbonFractionOfAsh * burntHelium / 12;
            poisons[StellarCoreModel.O16] = (1 - parameters.CarbonFractionOfAsh) * burntHelium / 16;
        }
    }

    public static SProcessConditions GetConditions(SProcessParameters parameters)
    {
        var temperature = parameters.TemperatureGK * 1e9;
        var heliumTotal = StellarCoreModel.InitialHelium(parameters.Metallicity);
        var burningYears = parameters.DurationYearsOverride ?? StellarCoreModel.DefaultHeliumBurningYears(parameters.StellarMass);
        return new SProcessConditions(
            parameters.TemperatureGK,
            KeVPerKelvin * temperature,
            parameters.DensityOverride ?? StellarCoreModel.DefaultDensity(parameters.StellarMass),
            parameters.DensityOverride is null,
            burningYears,
            parameters.DurationYearsOverride is null,
            burningYears * Math.Min(parameters.HeliumAtOnset, heliumTotal) / heliumTotal,
            Math.Sqrt(2 * Boltzmann * temperature / NeutronMass),
            NeutronSourceRates.Ne22AlphaN.Evaluate(parameters.TemperatureGK),
            NeutronSourceRates.Ne22AlphaGamma.Evaluate(parameters.TemperatureGK));
    }
}
