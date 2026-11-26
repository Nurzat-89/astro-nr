using System.Globalization;
using System.Text.RegularExpressions;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Infrastructure.RProcess;

/// <summary>Readers for the AME2020 and NUBASE2020 tables of the Atomic Mass Data Center and for user mass tables.</summary>
internal static partial class MassTableParsers
{
    /// <summary>
    /// AME2020 "mass_1.mas20": format a1,i3,i5,i5,i5,1x,a3,a4,1x,f14.6,... — N in columns 5–9, Z in 10–14,
    /// A in 15–19, mass excess (keV) in 29–42. A '#' instead of the decimal point marks an estimated value.
    /// </summary>
    /// <returns>Mass excess in MeV.</returns>
    public static Dictionary<Nuclide, double> ParseAme2020(TextReader reader)
    {
        var result = new Dictionary<Nuclide, double>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 42)
                continue;

            if (!TryInt(line.AsSpan(4, 5), out var n) || !TryInt(line.AsSpan(9, 5), out var z) || !TryInt(line.AsSpan(14, 5), out var a))
                continue;
            if (n + z != a)
                continue;

            var text = line.Substring(28, 14).Replace('#', '.').Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var keV))
                result[new Nuclide(z, a)] = keV / 1000.0;
        }

        return result;
    }

    /// <summary>
    /// NUBASE2020 "nubase_4.mas20": A in columns 1–3, ZZZi in 5–8 (i = 0 for the ground state),
    /// Jπ in 89–102. Returns the ground-state spin J where it is given (also estimated "#" values).
    /// </summary>
    public static Dictionary<Nuclide, double> ParseNubaseSpins(TextReader reader)
    {
        var result = new Dictionary<Nuclide, double>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || line.Length < 102)
                continue;
            if (!TryInt(line.AsSpan(0, 3), out var a) || !TryInt(line.AsSpan(4, 3), out var z) || line[7] != '0')
                continue;

            var match = SpinRegex().Match(line.Substring(88, 14));
            if (!match.Success)
                continue;

            var j = double.Parse(match.Groups["j"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["half"].Success)
                j /= 2;
            result[new Nuclide(z, a)] = j;
        }

        return result;
    }

    /// <summary>
    /// User mass table: one nuclide per line with Z, A and mass excess in MeV as the first three numbers
    /// (separated by blanks, tabs, commas or semicolons). Lines starting with '#' or without numbers are ignored.
    /// </summary>
    public static Dictionary<Nuclide, double> ParseUserTable(TextReader reader)
    {
        var result = new Dictionary<Nuclide, double>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            var tokens = trimmed.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3
                || !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z)
                || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                || !double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var mev))
            {
                if (result.Count == 0)
                    continue; // header lines before the data
                throw new FormatException($"Line {lineNumber}: expected 'Z A mass-excess(MeV)'.");
            }

            if (z < 0 || a < z || a > 400)
                throw new FormatException($"Line {lineNumber}: Z = {z}, A = {a} is not a nuclide.");
            result[new Nuclide(z, a)] = mev;
        }

        return result;
    }

    private static bool TryInt(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    [GeneratedRegex(@"(?<j>\d+)(?<half>/2)?")]
    private static partial Regex SpinRegex();
}
