using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Domain.Tests.SProcess;

public class SProcessPhysicsTests
{
    [Fact]
    public void Ne22AlphaN_MatchesReaclibFormulaAtT9_0_3()
    {
        // Sum of the three li12 sets evaluated by hand.
        double Set(double a0, double a1, double a3, double a4, double a5, double a6, double t9) =>
            Math.Exp(a0 + a1 / t9 + a3 * Math.Cbrt(t9) + a4 * t9 + a5 * Math.Pow(t9, 5.0 / 3) + a6 * Math.Log(t9));

        var expected =
            Set(-5.232600e+01, -5.550320e+00, 8.827250e+01, -4.015780e+01, -3.0, 0.0, 0.3) +
            Set(-1.047290e+01, -5.550320e+00, 1.548980e+01, -3.081540e+01, -3.0, 2.0, 0.3) +
            Set(-2.750270e+01, -7.386070e+00, 3.598700e+01, -4.121830e+00, 2.633260e-01, -1.5, 0.3);

        var rate = NeutronSourceRates.Ne22AlphaN.Evaluate(0.3);

        Assert.Equal(expected, rate, expected * 1e-12);
        Assert.InRange(rate, 1e-11, 1e-10); // ~3e-11 cm³/mol/s (Longland et al. 2012)
    }

    [Fact]
    public void Ne22AlphaN_RisesSteeplyWithTemperature()
    {
        var cool = NeutronSourceRates.Ne22AlphaN.Evaluate(0.2);
        var hot = NeutronSourceRates.Ne22AlphaN.Evaluate(0.3);

        Assert.True(hot / cool > 100, $"ratio {hot / cool}");
        Assert.True(NeutronSourceRates.Ne22AlphaGamma.Evaluate(0.3) > 0);
    }

    [Fact]
    public void Macs_InterpolatesLogLogAndClampsAtEdges()
    {
        var macs = new MaxwellianCrossSection([(10, 0.1), (100, 0.01)]);

        Assert.Equal(0.1, macs.Evaluate(5));
        Assert.Equal(0.01, macs.Evaluate(500));
        Assert.Equal(Math.Sqrt(0.1 * 0.01), macs.Evaluate(Math.Sqrt(10 * 100)), 12);
    }

    [Theory]
    [InlineData(1.0, "β-", 1, 0)]
    [InlineData(2.0, "β+/EC", -1, 0)]
    [InlineData(1.5, "β-,n", 1, -1)]
    [InlineData(4.0, "α", -2, -4)]
    public void BranchFromRtyp_DecodesDecayModes(double rtyp, string mode, int dz, int da)
    {
        var branch = RadioactiveDecay.BranchFromRtyp(rtyp, 0.5);

        Assert.Equal(mode, branch.Mode);
        Assert.Equal(dz, branch.DeltaZ);
        Assert.Equal(da, branch.DeltaA);
        Assert.Equal(0.5, branch.Fraction);
    }

    [Fact]
    public void Network_ReproducesAnalyticCaptureChain()
    {
        // ⁵⁶Fe (10 mb) → ⁵⁷Fe (30 mb) → ⁵⁸Fe (no data beyond): Bateman solution in exposure τ.
        var data = Data(
            macs: new() { [new(26, 56)] = 0.010, [new(26, 57)] = 0.030, [new(26, 58)] = 0.0 });
        var network = new CaptureNetwork(data, new Nuclide(26, 56), 42, 30);
        var y = new double[network.Count];
        y[network.IndexOf(new(26, 56))] = 1.0;

        const double tau = 0.02; // neutron exposure in mb⁻¹
        const int steps = 20000;
        var flux = 1e15;
        var dt = tau * 1e27 / flux / steps; // 1 mb⁻¹ = 10²⁷ cm⁻²
        for (var i = 0; i < steps; i++)
            network.Step(y, flux, dt);

        double s1 = 10, s2 = 30; // mb
        var expected56 = Math.Exp(-s1 * tau);
        var expected57 = s1 / (s2 - s1) * (Math.Exp(-s1 * tau) - Math.Exp(-s2 * tau));

        Assert.Equal(expected56, y[network.IndexOf(new(26, 56))], 3);
        Assert.Equal(expected57, y[network.IndexOf(new(26, 57))], 3);
        Assert.Equal(1.0, y.Sum(), 9); // ⁵⁸Fe has no capture: nothing leaves
    }

    [Fact]
    public void Network_FollowsBetaDecaysAndConservesParticles()
    {
        // ⁵⁸Fe → ⁵⁹Fe (β-, 44.5 d) → ⁵⁹Co → ⁶⁰Co ... ; ⁶⁰Co has no capture data beyond.
        var data = Data(
            macs: new() { [new(26, 58)] = 0.014, [new(26, 59)] = 0.010, [new(27, 59)] = 0.040, [new(27, 60)] = 0.050 },
            decays: new()
            {
                [new(26, 59)] = new RadioactiveDecay(44.5 * 86400, [RadioactiveDecay.BranchFromRtyp(1, 1.0)]),
                [new(27, 60)] = new RadioactiveDecay(5.27 * 3.156e7, [RadioactiveDecay.BranchFromRtyp(1, 1.0)]),
            });
        var network = new CaptureNetwork(data, new Nuclide(26, 58), 42, 30);
        var y = new double[network.Count];
        y[network.IndexOf(new(26, 58))] = 1.0;

        var lost = 0.0;
        for (var i = 0; i < 5000; i++)
            lost += network.Step(y, 1e15, 1e7).LostMoles;

        Assert.Contains(new Nuclide(27, 59), network.Species);
        Assert.True(y[network.IndexOf(new(27, 59))] > 0);
        Assert.Equal(1.0, y.Sum() + lost, 9);
    }

    [Fact]
    public void DecayShortLived_MovesShortLivedNucleiToTheirDaughters()
    {
        var data = Data(
            macs: new() { [new(26, 58)] = 0.014, [new(27, 59)] = 0.040 },
            decays: new() { [new(26, 59)] = new RadioactiveDecay(44.5 * 86400, [RadioactiveDecay.BranchFromRtyp(1, 1.0)]) });
        var network = new CaptureNetwork(data, new Nuclide(26, 58), 42, 30);
        var y = new double[network.Count];
        y[network.IndexOf(new(26, 59))] = 0.25;

        network.DecayShortLived(y, 3.156e13);

        Assert.Equal(0.0, y[network.IndexOf(new(26, 59))]);
        Assert.Equal(0.25, y[network.IndexOf(new(27, 59))], 12);
    }

    [Fact]
    public void Calculator_HotterCoreBurnsMoreNe22AndGivesLargerExposure()
    {
        var calculator = new SProcessCalculator(ChainData());

        var cool = calculator.Run(Parameters(0.22));
        var hot = calculator.Run(Parameters(0.30));

        Assert.InRange(cool.Ne22BurnedFraction, 0, 1);
        Assert.InRange(hot.Ne22BurnedFraction, 0, 1);
        Assert.True(hot.Ne22BurnedFraction > cool.Ne22BurnedFraction);
        Assert.True(hot.NeutronExposure > cool.NeutronExposure);
        Assert.True(hot.Fe56DestroyedFraction > cool.Fe56DestroyedFraction);
        Assert.True(hot.PeakNeutronDensity >= hot.MeanNeutronDensity);
        Assert.Equal(hot.Conditions.PhaseDurationYears, hot.History[^1].TimeYears, 6);
        Assert.Equal(hot.Conditions.DurationYears * 0.1 / (1 - 0.014), hot.Conditions.PhaseDurationYears, 6);
    }

    [Fact]
    public void Calculator_ConservesSeedNucleiAndReportsConditions()
    {
        var result = new SProcessCalculator(ChainData()).Run(Parameters(0.30));

        var initialFe = result.Abundances.Single(a => a.Nuclide == StellarCoreModel.Fe56).InitialMassFraction / 56;
        var finalMoles = result.Abundances.Sum(a => a.FinalMassFraction / a.Nuclide.A);
        var lostMolesUpperBound = result.MassFractionBeyondNetwork / 56;
        Assert.InRange(finalMoles, initialFe - lostMolesUpperBound - 1e-15, initialFe * (1 + 1e-9));

        Assert.Equal(25.85, result.Conditions.KTkeV, 0.01); // kT = 8.617e-8 keV/K × 3e8 K
        Assert.True(result.Conditions.DensityFromModel);
        Assert.Equal(StellarCoreModel.DefaultDensity(25), result.Conditions.Density);
    }

    [Fact]
    public void Calculator_RejectsInvalidParameters()
    {
        var calculator = new SProcessCalculator(ChainData());

        var ex = Assert.Throws<ArgumentException>(() => calculator.Run(new SProcessParameters { StellarMass = 2, TemperatureGK = 0.3 }));
        Assert.Contains("Stellar mass", ex.Message);
    }

    private static SProcessParameters Parameters(double t9) => new()
    {
        StellarMass = 25,
        TemperatureGK = t9,
        TimeSteps = 400,
    };

    /// <summary>Fe–Ni chain with plausible 30 keV MACS and poisons.</summary>
    private static SProcessNuclearData ChainData() => Data(
        macs: new()
        {
            [new(26, 56)] = 0.0115, [new(26, 57)] = 0.040, [new(26, 58)] = 0.014, [new(27, 59)] = 0.040,
            [new(28, 60)] = 0.030, [new(28, 61)] = 0.080, [new(28, 62)] = 0.023, [new(28, 63)] = 0.030,
            [new(6, 12)] = 1.5e-5, [new(8, 16)] = 3.8e-5, [new(10, 20)] = 1.2e-4, [new(10, 22)] = 5.8e-5,
            [new(12, 24)] = 3.3e-3, [new(12, 25)] = 6.4e-3, [new(12, 26)] = 1.3e-4,
        },
        decays: new()
        {
            [new(26, 59)] = new RadioactiveDecay(44.5 * 86400, [RadioactiveDecay.BranchFromRtyp(1, 1.0)]),
            [new(27, 60)] = new RadioactiveDecay(1.66e8, [RadioactiveDecay.BranchFromRtyp(1, 1.0)]),
            [new(28, 63)] = new RadioactiveDecay(3.2e9, [RadioactiveDecay.BranchFromRtyp(1, 1.0)]),
        });

    private static SProcessNuclearData Data(
        Dictionary<Nuclide, double> macs,
        Dictionary<Nuclide, RadioactiveDecay>? decays = null) =>
        new(
            macs.ToDictionary(p => p.Key, p => new MaxwellianCrossSection([(1, p.Value), (100, p.Value)])),
            decays ?? new Dictionary<Nuclide, RadioactiveDecay>(),
            "test data");
}
