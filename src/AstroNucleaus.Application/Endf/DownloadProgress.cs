namespace AstroNucleaus.Application.Endf;

public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Completed fraction between 0 and 1, or null when the total size is unknown.</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0.0, 1.0) : null;
}
