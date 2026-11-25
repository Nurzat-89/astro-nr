using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Infrastructure.Endf.Parsing;

/// <summary>CONT/HEAD record: [C1, C2, L1, L2, N1, N2].</summary>
internal readonly record struct ContRecord(double C1, double C2, int L1, int L2, int N1, int N2);

/// <summary>
/// Reads ENDF records (TEXT, CONT, LIST, TAB1) sequentially from the lines of one section.
/// </summary>
internal sealed class EndfRecordReader
{
    private readonly IReadOnlyList<EndfLine> _lines;
    private int _position;

    public EndfRecordReader(IReadOnlyList<EndfLine> lines)
    {
        _lines = lines;
    }

    public bool IsAtEnd => _position >= _lines.Count;

    public string ReadText() => Next().Data;

    public ContRecord ReadCont()
    {
        var line = Next();
        try
        {
            return new ContRecord(
                EndfNumber.ParseReal(line.Field(0)),
                EndfNumber.ParseReal(line.Field(1)),
                EndfNumber.ParseInt(line.Field(2)),
                EndfNumber.ParseInt(line.Field(3)),
                EndfNumber.ParseInt(line.Field(4)),
                EndfNumber.ParseInt(line.Field(5)));
        }
        catch (FormatException ex)
        {
            throw new EndfFormatException(ex.Message, line.LineNumber, ex);
        }
    }

    /// <summary>LIST record: CONT followed by N1 reals.</summary>
    public (ContRecord Head, double[] Values) ReadList()
    {
        var head = ReadCont();
        return (head, ReadReals(head.N1));
    }

    /// <summary>TAB1 record: CONT (N1 = NR, N2 = NP), NR (NBT, INT) pairs, NP (x, y) pairs.</summary>
    public (ContRecord Head, TabulatedFunction Function) ReadTab1()
    {
        var head = ReadCont();
        var regionCount = head.N1;
        var pointCount = head.N2;

        if (regionCount < 0 || pointCount < 0)
            throw new EndfFormatException($"Invalid TAB1 sizes NR={regionCount}, NP={pointCount}.", CurrentLineNumber);

        var interpolation = ReadInts(2 * regionCount);
        var regions = new InterpolationRegion[regionCount];
        for (var i = 0; i < regionCount; i++)
            regions[i] = new InterpolationRegion(interpolation[2 * i], ToLaw(interpolation[2 * i + 1]));

        var values = ReadReals(2 * pointCount);
        var x = new double[pointCount];
        var y = new double[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            x[i] = values[2 * i];
            y[i] = values[2 * i + 1];
        }

        if (regions.Length == 0)
            regions = [new InterpolationRegion(pointCount, InterpolationLaw.LinLin)];

        try
        {
            return (head, new TabulatedFunction(x, y, regions));
        }
        catch (ArgumentException ex)
        {
            throw new EndfFormatException($"Invalid TAB1 data: {ex.Message}", CurrentLineNumber, ex);
        }
    }

    public double[] ReadReals(int count)
    {
        var values = new double[count];
        try
        {
            for (var i = 0; i < count;)
            {
                var line = Next();
                for (var f = 0; f < EndfLine.FieldCount && i < count; f++, i++)
                    values[i] = EndfNumber.ParseReal(line.Field(f));
            }
        }
        catch (FormatException ex)
        {
            throw new EndfFormatException(ex.Message, CurrentLineNumber, ex);
        }

        return values;
    }

    public int[] ReadInts(int count)
    {
        var values = new int[count];
        try
        {
            for (var i = 0; i < count;)
            {
                var line = Next();
                for (var f = 0; f < EndfLine.FieldCount && i < count; f++, i++)
                    values[i] = EndfNumber.ParseInt(line.Field(f));
            }
        }
        catch (FormatException ex)
        {
            throw new EndfFormatException(ex.Message, CurrentLineNumber, ex);
        }

        return values;
    }

    private int CurrentLineNumber => _position > 0 ? _lines[_position - 1].LineNumber : 0;

    private EndfLine Next()
    {
        if (_position >= _lines.Count)
        {
            var last = _lines.Count > 0 ? _lines[^1] : default;
            throw new EndfFormatException(
                $"Unexpected end of section MF={last.MF}, MT={last.MT}.", last.LineNumber);
        }

        return _lines[_position++];
    }

    private static InterpolationLaw ToLaw(int code) =>
        Enum.IsDefined(typeof(InterpolationLaw), code) ? (InterpolationLaw)code : InterpolationLaw.LinLin;
}
