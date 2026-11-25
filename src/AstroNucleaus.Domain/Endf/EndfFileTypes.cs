namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// Names of ENDF-6 file types (MF numbers).
/// </summary>
public static class EndfFileTypes
{
    public static string GetName(int mf) => mf switch
    {
        1 => "General information",
        2 => "Resonance parameters",
        3 => "Reaction cross sections",
        4 => "Angular distributions",
        5 => "Energy distributions",
        6 => "Energy-angle distributions",
        7 => "Thermal neutron scattering",
        8 => "Decay data",
        9 => "Isomer production multiplicities",
        10 => "Isomer production cross sections",
        12 => "Photon production multiplicities",
        13 => "Photon production cross sections",
        14 => "Photon angular distributions",
        15 => "Photon energy distributions",
        23 => "Photo-atomic cross sections",
        26 => "Electro-atomic distributions",
        27 => "Atomic form factors",
        28 => "Atomic relaxation",
        30 => "Covariances: model parameters",
        31 => "Covariances: fission multiplicities",
        32 => "Covariances: resonance parameters",
        33 => "Covariances: cross sections",
        34 => "Covariances: angular distributions",
        35 => "Covariances: energy distributions",
        39 => "Covariances: isomer multiplicities",
        40 => "Covariances: isomer cross sections",
        _ => $"MF {mf}",
    };
}
