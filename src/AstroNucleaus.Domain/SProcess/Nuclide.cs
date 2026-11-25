using System.Globalization;
using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// A nuclide in its ground state, identified by proton number Z and mass number A.
/// Ordered by A, then Z (the order of a neutron-capture network).
/// </summary>
public readonly record struct Nuclide(int Z, int A) : IComparable<Nuclide>
{
    public int N => A - Z;

    public string Symbol => ChemicalElements.GetSymbol(Z) ?? $"Z{Z}";

    public int CompareTo(Nuclide other) => A != other.A ? A.CompareTo(other.A) : Z.CompareTo(other.Z);

    public override string ToString() => $"{Symbol}-{A.ToString(CultureInfo.InvariantCulture)}";
}
