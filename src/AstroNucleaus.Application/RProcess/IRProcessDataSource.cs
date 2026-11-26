using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Domain.RProcess;

namespace AstroNucleaus.Application.RProcess;

/// <summary>Which r-process input files are available.</summary>
/// <param name="ReaclibFile">Original name of the imported REACLIB file, or null.</param>
/// <param name="MassTableFile">Original name of the imported theoretical mass table, or null.</param>
public sealed record RProcessDataStatus(string? ReaclibFile, DateTime? ReaclibImported, string? MassTableFile, int? MassTableEntries)
{
    public bool HasReaclib => ReaclibFile is not null;
}

/// <summary>
/// Provides REACLIB rates, masses and spins for the r-process network.
/// REACLIB and the optional theoretical mass table are files the user imports; AME2020/NUBASE2020 are downloaded.
/// </summary>
public interface IRProcessDataSource
{
    RProcessDataStatus GetStatus();

    /// <summary>Validates and stores a REACLIB file (REACLIB 1 or 2 format).</summary>
    Task ImportReaclibAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Downloads the default REACLIB snapshot (pynucastro copy on GitHub), validates and stores it.</summary>
    Task DownloadReaclibAsync(IProgress<SProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Validates and stores a mass table with "Z A mass-excess(MeV)" lines.</summary>
    Task ImportMassTableAsync(string path, CancellationToken cancellationToken = default);

    void RemoveMassTable();

    Task<RProcessNuclearData> LoadAsync(IProgress<SProcessProgress>? progress = null, CancellationToken cancellationToken = default);
}
