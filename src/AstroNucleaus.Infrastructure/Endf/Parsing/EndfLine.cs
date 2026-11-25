namespace AstroNucleaus.Infrastructure.Endf.Parsing;

/// <summary>
/// One 80-column ENDF line: six 11-character data fields (columns 1-66),
/// MAT (67-70), MF (71-72), MT (73-75), and an optional sequence number (76-80).
/// </summary>
internal readonly struct EndfLine
{
    public const int FieldWidth = 11;
    public const int FieldCount = 6;
    public const int DataWidth = FieldWidth * FieldCount;
    private const int ControlWidth = 75;

    private EndfLine(string text, int mat, int mf, int mt, int lineNumber)
    {
        Text = text;
        Mat = mat;
        MF = mf;
        MT = mt;
        LineNumber = lineNumber;
    }

    /// <summary>Line text padded to at least 75 characters.</summary>
    public string Text { get; }

    public int Mat { get; }

    public int MF { get; }

    public int MT { get; }

    /// <summary>1-based line number in the file.</summary>
    public int LineNumber { get; }

    /// <summary>Text of the data area (columns 1-66).</summary>
    public string Data => Text[..DataWidth];

    public ReadOnlySpan<char> Field(int index) => Text.AsSpan(index * FieldWidth, FieldWidth);

    public static EndfLine Parse(string raw, int lineNumber)
    {
        var text = raw.Length >= ControlWidth ? raw : raw.PadRight(ControlWidth);
        try
        {
            var mat = EndfNumber.ParseInt(text.AsSpan(66, 4));
            var mf = EndfNumber.ParseInt(text.AsSpan(70, 2));
            var mt = EndfNumber.ParseInt(text.AsSpan(72, 3));
            return new EndfLine(text, mat, mf, mt, lineNumber);
        }
        catch (FormatException ex)
        {
            throw new EndfFormatException($"Invalid MAT/MF/MT control fields: {ex.Message}", lineNumber, ex);
        }
    }
}
