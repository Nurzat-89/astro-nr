using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Application.Endf;

/// <summary>
/// Reads ENDF-6 formatted data.
/// </summary>
public interface IEndfParser
{
    EndfTape Parse(TextReader reader);

    EndfTape ParseFile(string path);
}
