namespace AstroNucleaus.Infrastructure.Endf.Parsing;

/// <summary>
/// Thrown when ENDF data does not follow the ENDF-6 format.
/// </summary>
public sealed class EndfFormatException : Exception
{
    public EndfFormatException(string message, int lineNumber, Exception? innerException = null)
        : base(lineNumber > 0 ? $"Line {lineNumber}: {message}" : message, innerException)
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based line number where the problem was found (0 if unknown).</summary>
    public int LineNumber { get; }
}
