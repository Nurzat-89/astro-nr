using AstroNucleaus.Domain.RProcess;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Domain.Tests.RProcess;

public class RProcessPhysicsTests
{
    private static ReaclibRate Constant(double value) =>
        new("test", "test", "test", [Math.Log(value), 0, 0, 0, 0, 0, 0]);

    [Fact]
    public void CaptureAndPhotodisintegration_ReachSahaEquilibrium()
    {
        // ⁵⁶Fe ⇄ ⁵⁷Fe with S_n(⁵⁷Fe) = 2 MeV, J(⁵⁶Fe) = 0, J(⁵⁷Fe) = 1/2.
        var fe56 = new Nuclide(26, 56);
        var fe57 = new Nuclide(26, 57);
        const double sn = 2.0;
        var data = new RProcessNuclearData(
            new Dictionary<Nuclide, double> { [fe56] = -60.0, [fe57] = -60.0 + RProcessNuclearData.NeutronMassExcessMeV - sn },
            new Dictionary<Nuclide, double> { [fe56] = 0, [fe57] = 0.5 },
            new Dictionary<Nuclide, ReaclibRate> { [fe56] = Constant(1e8) },
            new Dictionary<Nuclide, ReaclibRate>(),
            new Dictionary<Nuclide, IReadOnlyList<BetaDecayChannel>>(),
            "test");
        var parameters = new RProcessParameters { TemperatureGK = 1.2, NeutronDensity = 1e22, DurationSeconds = 1, MaxZ = 40 };
        var network = new IsotopeChainNetwork(data, parameters);
        var y = new double[network.Count];
        y[network.IndexOf(fe56)] = 1.0;

        for (var i = 0; i < 200; i++)
            network.Step(y, 1.0, irradiate: true);

        // Saha equation from fundamental constants (cgs).
        const double hbar = 1.054571817e-27, mu = 1.66053906660e-24, k = 1.380649e-16, kMeV = 8.617333262e-11;
        var temperature = 1.2e9;
        var quantum = Math.Pow(2 * Math.PI * hbar * hbar / (mu * k * temperature), 1.5);
        var expected = 1e22 * quantum * Math.Pow(57.0 / 56.0, 1.5) * (2.0 / (2 * 1.0)) * Math.Exp(sn / (kMeV * temperature));

        var ratio = y[network.IndexOf(fe57)] / y[network.IndexOf(fe56)];
        Assert.Equal(expected, ratio, expected * 2e-3);
        Assert.Equal(1.0, y.Sum(), 1e-6); // rounding with Δt·λ(γ,n) ~ 10⁹
    }

    [Fact]
    public void BetaDecaysAndDelayedNeutrons_ConserveParticles()
    {
        var fe56 = new Nuclide(26, 56);
        var fe57 = new Nuclide(26, 57);
        var data = new RProcessNuclearData(
            new Dictionary<Nuclide, double>(),
            new Dictionary<Nuclide, double>(),
            new Dictionary<Nuclide, ReaclibRate> { [fe56] = Constant(1e6) },
            new Dictionary<Nuclide, ReaclibRate>(),
            new Dictionary<Nuclide, IReadOnlyList<BetaDecayChannel>>
            {
                [fe57] = [new(new Nuclide(27, 57), 0, 3.0, "b-"), new(new Nuclide(27, 56), 1, 1.0, "b-n")],
                [new Nuclide(27, 57)] = [new(new Nuclide(28, 57), 0, 2.0, "b-")], // Ni is beyond MaxZ = 27
            },
            "test");
        var parameters = new RProcessParameters { TemperatureGK = 1, NeutronDensity = 1e20, DurationSeconds = 1, MaxZ = 30 };
        var network = new IsotopeChainNetwork(data, parameters with { MaxZ = 30 });
        var y = new double[network.Count];
        y[network.IndexOf(fe56)] = 1.0;

        var lost = 0.0;
        for (var i = 0; i < 500; i++)
            lost += network.Step(y, 0.01, irradiate: true);

        Assert.True(y[network.IndexOf(new Nuclide(27, 56))] > 0, "β-delayed neutron daughter populated");
        Assert.True(y[network.IndexOf(new Nuclide(27, 57))] > 0);
        Assert.Equal(1.0, y.Sum() + lost, 9);
    }

    [Fact]
    public void Calculator_HigherNeutronDensityMovesThePathFurtherFromStability()
    {
        var data = LiquidDropData();
        var calculator = new RProcessCalculator(data);

        var low = calculator.Run(Parameters(1e20));
        var high = calculator.Run(Parameters(1e28));

        var lowFe = low.WaitingPoints.Single(n => n.Z == 26);
        var highFe = high.WaitingPoints.Single(n => n.Z == 26);
        Assert.True(highFe.A > lowFe.A, $"{highFe} vs {lowFe}");
        Assert.True(high.NeutronsCapturedPerSeed > low.NeutronsCapturedPerSeed);
        Assert.InRange(low.LostBeyondMaxZ, 0, 1);
        Assert.Equal(low.Parameters.DurationSeconds, low.History[^1].TimeSeconds, 9);
    }

    [Fact]
    public void Calculator_DecaysProductsBackToStability()
    {
        var result = new RProcessCalculator(LiquidDropData()).Run(Parameters(1e24));

        // Every nucleus left after decay is one without β decay in the data.
        var unstableLeft = result.Abundances.Where(a => !double.IsPositiveInfinity(a.BetaHalfLifeSeconds) && a.Decayed > 1e-12).ToList();
        Assert.Empty(unstableLeft);
        Assert.Equal(1.0, result.Abundances.Sum(a => a.Decayed) + result.LostBeyondMaxZ, 6);
        Assert.Equal(result.MassChains.Sum(c => c.Decayed), result.Abundances.Sum(a => a.Decayed), 12);
    }

    [Fact]
    public void Calculator_RecordsFramesOfBothPhases()
    {
        var result = new RProcessCalculator(LiquidDropData()).Run(Parameters(1e24));

        Assert.Equal(result.FrameInfos.Count, result.FrameAbundances.Count);
        Assert.All(result.FrameAbundances, f => Assert.Equal(result.Abundances.Count, f.Length));
        Assert.True(result.FrameInfos[0].IsIrradiation);
        Assert.False(result.FrameInfos[^1].IsIrradiation);
        Assert.Equal(result.Parameters.DurationSeconds + result.Parameters.DecayYears * RProcessCalculator.SecondsPerYear,
            result.FrameInfos[^1].TimeSeconds, 1e-6 * result.FrameInfos[^1].TimeSeconds);

        // Times increase; the first frame holds only the seed, the last equals the decayed abundances.
        for (var k = 1; k < result.FrameInfos.Count; k++)
            Assert.True(result.FrameInfos[k].TimeSeconds > result.FrameInfos[k - 1].TimeSeconds);
        var seed = result.Abundances.ToList().FindIndex(a => a.Nuclide == result.Parameters.Seed);
        Assert.Equal(1.0, result.FrameAbundances[0][seed], 12);
        for (var i = 0; i < result.Abundances.Count; i++)
            Assert.Equal(result.Abundances[i].Decayed, result.FrameAbundances[^1][i], 12);

        // The last irradiation frame is the freeze-out state.
        var freeze = result.FrameInfos.ToList().FindLastIndex(f => f.IsIrradiation);
        for (var i = 0; i < result.Abundances.Count; i++)
            Assert.Equal(result.Abundances[i].AtFreezeOut, result.FrameAbundances[freeze][i], 12);
    }

    [Fact]
    public void Calculator_RejectsMissingSeedRateAndBadParameters()
    {
        var calculator = new RProcessCalculator(LiquidDropData());

        Assert.Throws<InvalidOperationException>(() => calculator.Run(Parameters(1e22) with { Seed = new Nuclide(30, 200) }));
        Assert.Throws<ArgumentException>(() => calculator.Run(Parameters(1e22) with { TemperatureGK = 50 }));
    }

    private static RProcessParameters Parameters(double neutronDensity) => new()
    {
        TemperatureGK = 1.2,
        NeutronDensity = neutronDensity,
        DurationSeconds = 0.5,
        MaxZ = 36,
        TimeSteps = 200,
        DecayYears = 1,
    };

    /// <summary>
    /// Synthetic data for Z = 26–36: semi-empirical masses, constant capture rates, and β⁻ decay
    /// (half-life shrinking with neutron excess) for nuclei beyond the valley of stability.
    /// </summary>
    private static RProcessNuclearData LiquidDropData()
    {
        var masses = new Dictionary<Nuclide, double>();
        var captures = new Dictionary<Nuclide, ReaclibRate>();
        var decays = new Dictionary<Nuclide, IReadOnlyList<BetaDecayChannel>>();

        for (var z = 26; z <= 36; z++)
        {
            for (var a = 2 * z; a <= 3 * z + 10; a++)
            {
                var n = new Nuclide(z, a);
                masses[n] = SemiEmpiricalMassExcess(z, a);
                captures[n] = Constant(1e8);
                var stableN = (int)Math.Round(z * 1.15 + 1);
                var excess = (a - z) - stableN;
                if (excess > 0)
                    decays[n] = [new BetaDecayChannel(new Nuclide(z + 1, a), 0, 0.1 * excess, "b-")];
            }
        }

        return new RProcessNuclearData(masses, new Dictionary<Nuclide, double>(), captures,
            new Dictionary<Nuclide, ReaclibRate>(), decays, "liquid drop test data");
    }

    private static double SemiEmpiricalMassExcess(int z, int a)
    {
        var n = a - z;
        var binding = 15.75 * a - 17.8 * Math.Pow(a, 2.0 / 3) - 0.711 * z * (z - 1) / Math.Cbrt(a) - 23.7 * (n - z) * (n - z) / (double)a;
        return z * 7.28897 + n * RProcessNuclearData.NeutronMassExcessMeV - binding;
    }
}
