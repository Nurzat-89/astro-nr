namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// Reaction cross section from ENDF File 3 (MF=3).
/// </summary>
/// <param name="MT">Reaction type number.</param>
/// <param name="MassDifferenceQValue">QM: mass-difference Q value (eV).</param>
/// <param name="ReactionQValue">QI: reaction Q value for the lowest state (eV).</param>
/// <param name="BreakupFlag">LR: complex breakup flag.</param>
/// <param name="Data">Cross section in barns versus incident energy in eV.</param>
public sealed record CrossSection(
    int MT,
    double MassDifferenceQValue,
    double ReactionQValue,
    int BreakupFlag,
    TabulatedFunction Data)
{
    public string ReactionName => ReactionTypes.GetName(MT);

    public double ThresholdEnergy => Data.MinX;
}
