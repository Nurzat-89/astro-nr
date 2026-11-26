using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Domain.RProcess;

/// <summary>A β⁻ decay channel (possibly with delayed neutron emission) with its rate in 1/s.</summary>
/// <param name="DelayedNeutrons">Number of neutrons emitted after the β⁻ decay (0, 1, 2, ...).</param>
public sealed record BetaDecayChannel(Nuclide Daughter, int DelayedNeutrons, double RatePerSecond, string Label);

/// <summary>
/// Nuclear input of the r-process network: masses, ground-state spins and REACLIB rates.
/// </summary>
public sealed class RProcessNuclearData
{
    /// <summary>Mass excess of the free neutron in MeV (AME2020: 8071.31806 keV).</summary>
    public const double NeutronMassExcessMeV = 8.07131806;

    public RProcessNuclearData(
        IReadOnlyDictionary<Nuclide, double> massExcessMeV,
        IReadOnlyDictionary<Nuclide, double> groundStateSpin,
        IReadOnlyDictionary<Nuclide, ReaclibRate> neutronCapture,
        IReadOnlyDictionary<Nuclide, ReaclibRate> reversePhotodisintegration,
        IReadOnlyDictionary<Nuclide, IReadOnlyList<BetaDecayChannel>> betaDecays,
        string description)
    {
        MassExcessMeV = massExcessMeV;
        GroundStateSpin = groundStateSpin;
        NeutronCapture = neutronCapture;
        ReversePhotodisintegration = reversePhotodisintegration;
        BetaDecays = betaDecays;
        Description = description;
    }

    /// <summary>Mass excess in MeV (AME2020, completed by a theoretical table where given).</summary>
    public IReadOnlyDictionary<Nuclide, double> MassExcessMeV { get; }

    /// <summary>Ground-state spin J; the statistical weight is 2J + 1.</summary>
    public IReadOnlyDictionary<Nuclide, double> GroundStateSpin { get; }

    /// <summary>(n,γ) rates keyed by the target (Z, A) → (Z, A+1).</summary>
    public IReadOnlyDictionary<Nuclide, ReaclibRate> NeutronCapture { get; }

    /// <summary>REACLIB (γ,n) reverse rates keyed by the parent (Z, A) → (Z, A−1) + n; used where masses are missing.</summary>
    public IReadOnlyDictionary<Nuclide, ReaclibRate> ReversePhotodisintegration { get; }

    /// <summary>β⁻ decay channels of each unstable nucleus.</summary>
    public IReadOnlyDictionary<Nuclide, IReadOnlyList<BetaDecayChannel>> BetaDecays { get; }

    public string Description { get; }

    /// <summary>Neutron separation energy S_n(Z, A) = ME(Z, A−1) + ME(n) − ME(Z, A) in MeV, if both masses are known.</summary>
    public double? NeutronSeparationEnergy(Nuclide nuclide)
    {
        if (MassExcessMeV.TryGetValue(nuclide, out var me) && MassExcessMeV.TryGetValue(nuclide with { A = nuclide.A - 1 }, out var lighter))
            return lighter + NeutronMassExcessMeV - me;
        return null;
    }

    /// <summary>Statistical weight 2J + 1 (1 when the spin is unknown).</summary>
    public double StatisticalWeight(Nuclide nuclide) =>
        GroundStateSpin.TryGetValue(nuclide, out var j) && j >= 0 ? 2 * j + 1 : 1.0;
}
