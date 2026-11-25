using System.Globalization;
using System.Text.RegularExpressions;
using AstroNucleaus.Domain.Endf;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Infrastructure.SProcess;

/// <summary>
/// Reads the comma-separated MACS tables of B. Pritychenko and S. F. Mughabghab, Nucl. Data Sheets 113, 3120 (2012),
/// published by NNDC at https://www.nndc.bnl.gov/astro/. One file per temperature; columns used:
/// isotope name (5th), kT in eV (6th), MACS in barns (7th). Isomers are skipped.
/// </summary>
internal static partial class MacsTableParser
{
    public static IEnumerable<(Nuclide Nuclide, double KTkeV, double Barns)> Parse(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            var columns = line.Split(',');
            if (columns.Length < 7)
                continue;

            if (TryParseNuclide(columns[4], out var nuclide)
                && TryParseKTkeV(columns[5], out var kT)
                && double.TryParse(columns[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var barns))
            {
                yield return (nuclide, kT, barns);
            }
        }
    }

    /// <summary>"26-Fe- 56" (ENDF/B-VII.1 files) or "Fe-56" (EAF-2010 files); isomers return false.</summary>
    internal static bool TryParseNuclide(string text, out Nuclide nuclide)
    {
        nuclide = default;
        var match = NameRegex().Match(text.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!match.Success || match.Groups["iso"].Success)
            return false;

        if (ChemicalElements.GetAtomicNumber(match.Groups["sym"].Value) is not { } z)
            return false;

        nuclide = new Nuclide(z, int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture));
        return nuclide.A >= z;
    }

    private static bool TryParseKTkeV(string text, out double kTkeV)
    {
        var value = text.Replace("eV", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var ok = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var eV) && eV > 0;
        kTkeV = eV / 1000;
        return ok;
    }

    [GeneratedRegex(@"^(?:\d+-)?(?<sym>[A-Za-z]{1,2})-(?<a>\d{1,3})(?<iso>[MmNn]\d*)?$")]
    private static partial Regex NameRegex();
}
