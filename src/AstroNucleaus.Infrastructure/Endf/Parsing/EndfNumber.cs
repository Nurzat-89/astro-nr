using System.Globalization;

namespace AstroNucleaus.Infrastructure.Endf.Parsing;

/// <summary>
/// Parses the 11-character numeric fields of ENDF records.
/// </summary>
internal static class EndfNumber
{
    /// <summary>
    /// Parses a real number. Accepts the compact Fortran form without an exponent letter
    /// ("1.234567+5", "-2.5-10"), standard E/D notation ("1.0E+06", "2.0D+02"), plain decimals,
    /// and blank fields (0).
    /// </summary>
    public static double ParseReal(ReadOnlySpan<char> field)
    {
        field = field.Trim();
        if (field.IsEmpty)
            return 0.0;

        Span<char> buffer = stackalloc char[field.Length + 1];
        var length = 0;
        var hasExponentLetter = false;

        foreach (var c in field)
        {
            if (c == ' ')
                continue; // some files contain blanks inside the field, e.g. "1.0000 +5"

            if (c is 'D' or 'd' or 'E' or 'e')
            {
                buffer[length++] = 'E';
                hasExponentLetter = true;
                continue;
            }

            buffer[length++] = c;
        }

        if (!hasExponentLetter)
        {
            // Insert 'E' before a sign that is not the leading sign: "1.234567+5" -> "1.234567E+5".
            for (var i = length - 1; i > 0; i--)
            {
                if (buffer[i] is '+' or '-')
                {
                    buffer[i..length].CopyTo(buffer[(i + 1)..]);
                    buffer[i] = 'E';
                    length++;
                    break;
                }
            }
        }

        if (!double.TryParse(buffer[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"'{field.ToString()}' is not a valid ENDF real number.");

        return value;
    }

    /// <summary>Parses an integer field; blank fields are 0.</summary>
    public static int ParseInt(ReadOnlySpan<char> field)
    {
        field = field.Trim();
        if (field.IsEmpty)
            return 0;

        if (int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        // A few evaluations write integer fields in real format, e.g. "1.000000+0".
        return (int)Math.Round(ParseReal(field));
    }
}
