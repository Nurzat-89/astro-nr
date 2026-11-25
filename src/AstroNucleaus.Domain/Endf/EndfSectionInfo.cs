namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// A section (MF/MT pair) present in a material, whether or not its content was parsed.
/// </summary>
public sealed record EndfSectionInfo(int MF, int MT, int LineCount)
{
    public string FileType => EndfFileTypes.GetName(MF);

    public string ReactionName => MF == 1 ? string.Empty : ReactionTypes.GetName(MT);
}
