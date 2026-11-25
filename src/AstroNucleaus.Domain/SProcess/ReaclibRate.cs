namespace AstroNucleaus.Domain.SProcess;

/// <summary>
/// Thermonuclear reaction rate N_A⟨σv⟩ in REACLIB form: a sum of sets
/// exp[a0 + a1/T9 + a2/T9^(1/3) + a3·T9^(1/3) + a4·T9 + a5·T9^(5/3) + a6·ln T9], in cm³ mol⁻¹ s⁻¹.
/// </summary>
public sealed class ReaclibRate
{
    private readonly double[][] _sets;

    public ReaclibRate(string reaction, string label, string reference, params double[][] sets)
    {
        if (sets.Length == 0 || sets.Any(s => s.Length != 7))
            throw new ArgumentException("Each REACLIB set needs exactly 7 coefficients.", nameof(sets));

        Reaction = reaction;
        Label = label;
        Reference = reference;
        _sets = sets.Select(s => s.ToArray()).ToArray();
    }

    public string Reaction { get; }

    public string Label { get; }

    public string Reference { get; }

    /// <summary>N_A⟨σv⟩ in cm³ mol⁻¹ s⁻¹ at temperature <paramref name="t9"/> (GK).</summary>
    public double Evaluate(double t9)
    {
        if (t9 <= 0)
            throw new ArgumentOutOfRangeException(nameof(t9), t9, "Temperature must be positive.");

        var t13 = Math.Cbrt(t9);
        var t53 = t9 * t13 * t13;
        var lnT9 = Math.Log(t9);
        var sum = 0.0;
        foreach (var a in _sets)
            sum += Math.Exp(a[0] + a[1] / t9 + a[2] / t13 + a[3] * t13 + a[4] * t9 + a[5] * t53 + a[6] * lnT9);
        return sum;
    }
}

/// <summary>
/// Rates of the ²²Ne neutron source, copied from the JINA REACLIB database (recommended set "li12",
/// R. Longland, C. Iliadis, A. I. Karakas, Phys. Rev. C 85, 065809 (2012); REACLIB: Cyburt et al., ApJS 189, 240 (2010)).
/// </summary>
public static class NeutronSourceRates
{
    public static ReaclibRate Ne22AlphaN { get; } = new(
        "22Ne(α,n)25Mg", "li12", "Longland et al. 2012 via JINA REACLIB",
        [-5.232600e+01, -5.550320e+00, 0.0, 8.827250e+01, -4.015780e+01, -3.000000e+00, 0.0],
        [-1.047290e+01, -5.550320e+00, 0.0, 1.548980e+01, -3.081540e+01, -3.000000e+00, 2.000000e+00],
        [-2.750270e+01, -7.386070e+00, 0.0, 3.598700e+01, -4.121830e+00, 2.633260e-01, -1.500000e+00]);

    public static ReaclibRate Ne22AlphaGamma { get; } = new(
        "22Ne(α,γ)26Mg", "li12", "Longland et al. 2012 via JINA REACLIB",
        [-9.240770e+01, -9.104770e-01, 0.0, 0.0, 0.0, 0.0, -1.500000e+00],
        [-4.286400e+01, -2.221150e+00, 0.0, 0.0, 0.0, 0.0, -1.500000e+00],
        [-2.375270e+01, -3.882170e+00, 0.0, 0.0, 0.0, 0.0, -1.500000e+00],
        [-2.931460e+01, -6.447720e+00, 0.0, 4.326540e+01, -1.859820e+01, 2.801010e+00, -1.500000e+00],
        [-3.472540e+01, -6.364210e+00, 0.0, 3.598780e+01, -4.106840e+00, 2.593450e-01, -1.500000e+00]);
}
