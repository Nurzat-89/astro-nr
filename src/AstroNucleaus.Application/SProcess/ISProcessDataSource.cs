using AstroNucleaus.Application.Endf;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Application.SProcess;

/// <summary>
/// Provides capture cross sections (MACS) and decay data for the s-process network.
/// </summary>
public interface ISProcessDataSource
{
    /// <summary>Loads (downloading on first use) the data for nuclides from Fe up to <paramref name="maxZ"/>.</summary>
    Task<SProcessNuclearData> LoadAsync(
        int maxZ,
        IProgress<SProcessProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Progress of a calculation: a stage description and, when known, the completed fraction.</summary>
public readonly record struct SProcessProgress(string Stage, double? Fraction = null)
{
    public static SProcessProgress FromDownload(string what, DownloadProgress download) =>
        new($"Downloading {what}: {download.BytesReceived / (1024.0 * 1024.0):F1} MB"
            + (download.TotalBytes is { } total ? $" of {total / (1024.0 * 1024.0):F1} MB" : string.Empty),
            download.Fraction);
}
