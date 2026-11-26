using System.Globalization;
using System.Text.RegularExpressions;
using AstroNucleaus.Domain.Endf;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Infrastructure.RProcess;

/// <summary>One REACLIB set: 7 coefficients of one rate component.</summary>
/// <param name="ResonanceFlag">'n' non-resonant, 'r' resonant, 'w' weak, 's' spontaneous.</param>
/// <param name="IsReverse">'v' flag: rate computed from the forward rate by detailed balance.</param>
internal sealed record ReaclibSet(
    int Chapter,
    IReadOnlyList<Nuclide> Reactants,
    IReadOnlyList<Nuclide> Products,
    string Label,
    char ResonanceFlag,
    bool IsReverse,
    double QValue,
    double[] Coefficients)
{
    public bool IsWeak => ResonanceFlag == 'w';
}

/// <summary>
/// Reads JINA REACLIB files (Cyburt et al., ApJS 189, 240 (2010)) in both layouts: "REACLIB 1" (a chapter number
/// followed by many sets) and "REACLIB 2" (a chapter number before every set). Each set has three lines:
/// 5 blanks, six nuclide names (a5 each), 8 blanks, label (a4), resonance flag (a1), reverse flag (a1), 3 blanks,
/// Q value (e12.5); then a0–a3 and a4–a6 (e13.6 each). Sets with an isomer or unknown nuclide are skipped.
/// </summary>
internal static partial class ReaclibParser
{
    public static readonly Nuclide Neutron = new(0, 1);

    /// <summary>Number of reactants per REACLIB chapter (index = chapter).</summary>
    private static readonly int[] ReactantCount = [0, 1, 1, 1, 2, 2, 2, 2, 3, 3, 4, 1];

    public static IEnumerable<ReaclibSet> Parse(TextReader reader)
    {
        var chapter = 0;
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                if (number is < 1 or > 11)
                    throw new FormatException($"'{trimmed}' is not a REACLIB chapter number (1–11).");
                chapter = number;
                continue;
            }

            if (chapter == 0)
                throw new FormatException("The file does not start with a REACLIB chapter number.");

            var first = reader.ReadLine() ?? throw new FormatException("Unexpected end of REACLIB file.");
            var second = reader.ReadLine() ?? throw new FormatException("Unexpected end of REACLIB file.");
            var set = ParseSet(chapter, line, first, second);
            if (set is not null)
                yield return set;
        }
    }

    private static ReaclibSet? ParseSet(int chapter, string header, string coefficients1, string coefficients2)
    {
        header = header.PadRight(74);
        var names = Enumerable.Range(0, 6)
            .Select(i => header.Substring(5 + 5 * i, 5).Trim())
            .Where(n => n.Length > 0)
            .ToList();

        var label = header.Substring(43, 4).Trim();
        var resonance = char.ToLowerInvariant(header[47]);
        var reverse = char.ToLowerInvariant(header[48]) == 'v';
        double.TryParse(header[52..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);

        var coefficients = Fields(coefficients1, 4).Concat(Fields(coefficients2, 3)).ToArray();

        var nuclides = new List<Nuclide>(names.Count);
        foreach (var name in names)
        {
            if (TryParseNuclide(name) is not { } nuclide)
                return null;
            nuclides.Add(nuclide);
        }

        var reactants = ReactantCount[chapter];
        if (nuclides.Count <= reactants)
            return null;

        return new ReaclibSet(chapter, nuclides.Take(reactants).ToList(), nuclides.Skip(reactants).ToList(),
            label, resonance, reverse, q, coefficients);
    }

    private static IEnumerable<double> Fields(string line, int count)
    {
        line = line.PadRight(13 * count);
        for (var i = 0; i < count; i++)
        {
            var text = line.Substring(13 * i, 13).Trim();
            yield return text.Length == 0
                ? 0
                : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>"n", "p", "d", "t", "he4", "fe56", ...; isomers such as "al-6" or "al*6" return null.</summary>
    internal static Nuclide? TryParseNuclide(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "n": return Neutron;
            case "p": return new Nuclide(1, 1);
            case "d": return new Nuclide(1, 2);
            case "t": return new Nuclide(1, 3);
        }

        var match = NameRegex().Match(name);
        if (!match.Success || ChemicalElements.GetAtomicNumber(match.Groups["sym"].Value) is not { } z)
            return null;

        return new Nuclide(z, int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("^(?<sym>[A-Za-z]{1,2})(?<a>[0-9]{1,3})$")]
    private static partial Regex NameRegex();
}
