namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// A parsed ENDF file ("tape") containing one or more materials.
/// </summary>
/// <param name="Identification">TPID text of the first line.</param>
/// <param name="TapeNumber">NTAPE from the TPID line.</param>
/// <param name="Materials">Materials in file order.</param>
/// <param name="Warnings">Problems found in sections that could not be parsed.</param>
public sealed record EndfTape(
    string Identification,
    int TapeNumber,
    IReadOnlyList<EndfMaterial> Materials,
    IReadOnlyList<string> Warnings);
