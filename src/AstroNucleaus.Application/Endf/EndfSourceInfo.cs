namespace AstroNucleaus.Application.Endf;

/// <summary>
/// A server that provides ENDF libraries, e.g. NNDC or IAEA.
/// </summary>
/// <param name="Id">Stable identifier, also used as the cache folder name.</param>
/// <param name="Name">Display name.</param>
public sealed record EndfSourceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}
