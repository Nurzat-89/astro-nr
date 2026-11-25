namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// ENDF-6 interpolation scheme (INT) between two tabulated points.
/// </summary>
public enum InterpolationLaw
{
    /// <summary>INT=1: y is constant (histogram), equal to the value at the left point.</summary>
    Histogram = 1,

    /// <summary>INT=2: y is linear in x.</summary>
    LinLin = 2,

    /// <summary>INT=3: y is linear in ln(x).</summary>
    LinLog = 3,

    /// <summary>INT=4: ln(y) is linear in x.</summary>
    LogLin = 4,

    /// <summary>INT=5: ln(y) is linear in ln(x).</summary>
    LogLog = 5,

    /// <summary>INT=6: charged-particle penetrability law (evaluated as lin-lin here).</summary>
    ChargedParticle = 6,
}
