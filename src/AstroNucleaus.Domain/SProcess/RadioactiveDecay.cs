namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// One decay branch: the daughter is (Z + DeltaZ, A + DeltaA). Fission branches have no daughter in the network.
/// </summary>
/// <param name="Mode">ENDF decay mode, e.g. "β-", "β+/EC", "β-,n", "α".</param>
public sealed record DecayBranch(string Mode, double Fraction, int DeltaZ, int DeltaA, bool IsFission = false);

/// <summary>
/// Ground-state radioactive decay data (ENDF File 8, MT=457).
/// </summary>
public sealed record RadioactiveDecay(double HalfLifeSeconds, IReadOnlyList<DecayBranch> Branches)
{
    public static RadioactiveDecay Stable { get; } = new(double.PositiveInfinity, []);

    public bool IsStable => double.IsPositiveInfinity(HalfLifeSeconds);

    /// <summary>λ = ln 2 / T½ in 1/s; 0 for stable nuclides.</summary>
    public double DecayConstant => IsStable || HalfLifeSeconds <= 0 ? 0.0 : Math.Log(2) / HalfLifeSeconds;

    /// <summary>
    /// Decodes an ENDF RTYP value: each digit is one step, e.g. 1 = β-, 2 = β+/EC, 1.5 = β- followed by neutron emission.
    /// </summary>
    public static DecayBranch BranchFromRtyp(double rtyp, double fraction)
    {
        var digits = rtyp.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture).Replace(".", string.Empty, StringComparison.Ordinal);
        int dz = 0, da = 0;
        var modes = new List<string>();
        var fission = false;

        foreach (var digit in digits)
        {
            switch (digit)
            {
                case '1': dz += 1; modes.Add("β-"); break;
                case '2': dz -= 1; modes.Add("β+/EC"); break;
                case '3': modes.Add("IT"); break;
                case '4': dz -= 2; da -= 4; modes.Add("α"); break;
                case '5': da -= 1; modes.Add("n"); break;
                case '6': fission = true; modes.Add("SF"); break;
                case '7': dz -= 1; da -= 1; modes.Add("p"); break;
                case '0': break;
                default: modes.Add($"RTYP {digit}"); break;
            }
        }

        return new DecayBranch(modes.Count > 0 ? string.Join(",", modes) : "unknown", fraction, dz, da, fission);
    }
}
