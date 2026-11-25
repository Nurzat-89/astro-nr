namespace AstroNucleaus.Domain.Endf;

/// <summary>
/// Names of ENDF reaction types (MT numbers). "z" stands for the incident particle.
/// </summary>
public static class ReactionTypes
{
    public const int Total = 1;
    public const int Elastic = 2;
    public const int Fission = 18;
    public const int RadiativeCapture = 102;

    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "(z,total)",
        [2] = "(z,elastic)",
        [3] = "(z,nonelastic)",
        [4] = "(z,n)",
        [5] = "(z,anything)",
        [10] = "(z,continuum)",
        [11] = "(z,2nd)",
        [16] = "(z,2n)",
        [17] = "(z,3n)",
        [18] = "(z,fission)",
        [19] = "(z,f) first chance",
        [20] = "(z,nf) second chance",
        [21] = "(z,2nf) third chance",
        [22] = "(z,nα)",
        [23] = "(z,n3α)",
        [24] = "(z,2nα)",
        [25] = "(z,3nα)",
        [27] = "(z,absorption)",
        [28] = "(z,np)",
        [29] = "(z,n2α)",
        [32] = "(z,nd)",
        [33] = "(z,nt)",
        [34] = "(z,n³He)",
        [37] = "(z,4n)",
        [38] = "(z,3nf) fourth chance",
        [41] = "(z,2np)",
        [42] = "(z,3np)",
        [44] = "(z,n2p)",
        [45] = "(z,npα)",
        [91] = "(z,n) continuum",
        [101] = "(z,disappearance)",
        [102] = "(z,γ)",
        [103] = "(z,p)",
        [104] = "(z,d)",
        [105] = "(z,t)",
        [106] = "(z,³He)",
        [107] = "(z,α)",
        [108] = "(z,2α)",
        [109] = "(z,3α)",
        [111] = "(z,2p)",
        [112] = "(z,pα)",
        [113] = "(z,t2α)",
        [114] = "(z,d2α)",
        [115] = "(z,pd)",
        [116] = "(z,pt)",
        [117] = "(z,dα)",
        [151] = "Resonance parameters",
        [201] = "(z,Xn) neutron production",
        [202] = "(z,Xγ) photon production",
        [203] = "(z,Xp) proton production",
        [204] = "(z,Xd) deuteron production",
        [205] = "(z,Xt) triton production",
        [206] = "(z,X³He) ³He production",
        [207] = "(z,Xα) α production",
        [301] = "Heating (KERMA)",
        [444] = "Damage energy",
        [452] = "ν total",
        [455] = "ν delayed",
        [456] = "ν prompt",
        [458] = "Fission energy release",
        [649] = "(z,p) continuum",
        [699] = "(z,d) continuum",
        [749] = "(z,t) continuum",
        [799] = "(z,³He) continuum",
        [849] = "(z,α) continuum",
    };

    public static string GetName(int mt)
    {
        if (Names.TryGetValue(mt, out var name))
            return name;

        return mt switch
        {
            >= 51 and <= 90 => $"(z,n{mt - 50}) level {mt - 50}",
            >= 600 and < 649 => $"(z,p{mt - 600}) level {mt - 600}",
            >= 650 and < 699 => $"(z,d{mt - 650}) level {mt - 650}",
            >= 700 and < 749 => $"(z,t{mt - 700}) level {mt - 700}",
            >= 750 and < 799 => $"(z,³He{mt - 750}) level {mt - 750}",
            >= 800 and < 849 => $"(z,α{mt - 800}) level {mt - 800}",
            >= 875 and <= 891 => $"(z,2n{mt - 875}) level {mt - 875}",
            _ => $"MT {mt}",
        };
    }
}
