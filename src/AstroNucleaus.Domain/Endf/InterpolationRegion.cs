namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// One interpolation range of a TAB1 record.
/// </summary>
/// <param name="LastPoint">NBT: 1-based index of the last point that belongs to this region.</param>
/// <param name="Law">INT: interpolation law used inside this region.</param>
public sealed record InterpolationRegion(int LastPoint, InterpolationLaw Law);
