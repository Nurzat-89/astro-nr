namespace AstroNucleaus.Infrastructure.Endf;

/// <summary>
/// Settings for downloading ENDF libraries.
/// </summary>
public sealed class EndfOptions
{
    /// <summary>Folder where downloaded evaluations are unpacked and cached.</summary>
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstroNucleaus", "endf");

    /// <summary>Timeout of one HTTP request (large evaluations are tens of MB).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>User-Agent header sent to the servers.</summary>
    public string UserAgent { get; set; } = "AstroNucleaus/1.0";

    /// <summary>
    /// Root of the IAEA Nuclear Data Section archive, laid out as {library}/{sublibrary}/{file}.zip.
    /// Mirrors with the same layout can be used instead.
    /// </summary>
    public Uri IaeaBaseAddress { get; set; } = new("https://www-nds.iaea.org/public/download-endf/");

    /// <summary>
    /// NNDC tables of Maxwellian-averaged (n,γ) cross sections at 60 temperatures, from ENDF/B-VII.1 and EAF-2010
    /// (B. Pritychenko, S. F. Mughabghab, Nucl. Data Sheets 113, 3120 (2012)).
    /// </summary>
    public Uri MacsArchiveAddress { get; set; } = new("https://www.nndc.bnl.gov/astro/ENDFB71-EAF2010.zip");

    /// <summary>ENDF/B-VIII.0 radioactive decay sub-library (half-lives and decay modes).</summary>
    public Uri DecayArchiveAddress { get; set; } = new("https://www.nndc.bnl.gov/endf-b8.0/zips/ENDF-B-VIII.0_decay.zip");

    /// <summary>
    /// NNDC libraries. Each sub-library is one zip archive on the server; single evaluations are
    /// read from it with HTTP range requests.
    /// </summary>
    public List<NndcLibraryOptions> NndcLibraries { get; set; } =
    [
        new()
        {
            Name = "ENDF-B-VIII.0",
            ArchiveUrlTemplate = "https://www.nndc.bnl.gov/endf-b8.0/zips/ENDF-B-VIII.0_{sublibrary}.zip",
            Sublibraries =
            [
                "neutrons", "protons", "deuterons", "tritons", "helium3s", "alphas", "gammas", "decay",
                "nfy", "sfy", "thermal_scatt", "standards", "photoat", "atomic_relax", "electrons",
            ],
        },
        new()
        {
            Name = "ENDF-B-VII.1",
            ArchiveUrlTemplate = "https://www.nndc.bnl.gov/endf-b7.1/zips/ENDF-B-VII.1-{sublibrary}.zip",
            Sublibraries =
            [
                "neutrons", "protons", "deuterons", "tritons", "helium3s", "gammas", "decay",
                "nfy", "sfy", "thermal_scatt", "standards", "photoat", "atomic_relax", "electrons",
            ],
        },
    ];
}

/// <summary>
/// One NNDC library whose sub-libraries are published as zip archives.
/// </summary>
public sealed class NndcLibraryOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Archive URL with a "{sublibrary}" placeholder.</summary>
    public string ArchiveUrlTemplate { get; set; } = string.Empty;

    public List<string> Sublibraries { get; set; } = [];

    public Uri GetArchiveUri(string sublibrary) =>
        new(ArchiveUrlTemplate.Replace("{sublibrary}", Uri.EscapeDataString(sublibrary), StringComparison.Ordinal));
}
