namespace AstroNucleaus.Application.Endf;

/// <summary>
/// Downloads an evaluation file into the local cache.
/// </summary>
public interface IEndfFileDownloader
{
    /// <summary>
    /// Downloads and unpacks <paramref name="file"/> unless it is already cached.
    /// </summary>
    /// <returns>Full path of the local ENDF file.</returns>
    Task<string> DownloadAsync(
        EndfRemoteFile file,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
