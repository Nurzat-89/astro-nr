using System.Globalization;
using System.Text;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

/// <summary>
/// Builds ENDF-6 text with exact column layout for parser tests.
/// </summary>
internal sealed class EndfTestTape
{
    private readonly StringBuilder _text = new();

    /// <summary>Real field in E notation, with as many digits as fit into 11 columns.</summary>
    public static string R(double value)
    {
        var text = value.ToString("0.000000E+0", CultureInfo.InvariantCulture);
        if (text.Length > 11)
            text = value.ToString("0.00000E+0", CultureInfo.InvariantCulture);
        return text.PadLeft(11);
    }

    public static string I(int value) => value.ToString(CultureInfo.InvariantCulture).PadLeft(11);

    public static string Blank => new(' ', 11);

    public EndfTestTape Line(string data, int mat, int mf, int mt)
    {
        if (data.Length > 66)
            throw new ArgumentException($"Data area is {data.Length} characters: '{data}'.", nameof(data));

        _text.Append(data.PadRight(66))
            .Append(mat.ToString(CultureInfo.InvariantCulture).PadLeft(4))
            .Append(mf.ToString(CultureInfo.InvariantCulture).PadLeft(2))
            .Append(mt.ToString(CultureInfo.InvariantCulture).PadLeft(3))
            .Append("    1")
            .Append('\n');
        return this;
    }

    public EndfTestTape Cont(double c1, double c2, int l1, int l2, int n1, int n2, int mat, int mf, int mt) =>
        Line(R(c1) + R(c2) + I(l1) + I(l2) + I(n1) + I(n2), mat, mf, mt);

    public EndfTestTape Send(int mat, int mf) => Cont(0, 0, 0, 0, 0, 0, mat, mf, 0);

    public EndfTestTape Fend(int mat) => Cont(0, 0, 0, 0, 0, 0, mat, 0, 0);

    public EndfTestTape Mend() => Cont(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public EndfTestTape Tend() => Cont(0, 0, 0, 0, 0, 0, -1, 0, 0);

    public override string ToString() => _text.ToString();

    /// <summary>
    /// A small H-1-like incident-neutron evaluation: MF1/MT451, MF3/MT1 (Fortran-format numbers),
    /// MF3/MT102 with two interpolation regions, and an unparsed MF4/MT2 section.
    /// </summary>
    public static string CreateSample()
    {
        const int mat = 125;
        var tape = new EndfTestTape();

        tape.Line(" Test tape for AstroNucleaus", 1, 0, 0);

        // MF=1, MT=451
        tape.Cont(1001, 0.9991673, -1, 0, 0, 2, mat, 1, 451);
        tape.Cont(0, 0, 0, 0, 0, 6, mat, 1, 451);
        tape.Cont(1, 2.0e7, 1, 0, 10, 8, mat, 1, 451);
        tape.Cont(0, 0, 0, 0, 3, 2, mat, 1, 451);
        tape.Line(" 1-H -  1 ".PadRight(11) + "LANL".PadRight(11) + "EVAL-JUL16" + " " + "G.M.Hale", mat, 1, 451);
        tape.Line(" " + "Ref. 1".PadRight(21) + "DIST-FEB18" + " " + "REV1-JAN17" + new string(' ', 12) + "20180206", mat, 1, 451);
        tape.Line(" Sample comment line", mat, 1, 451);
        tape.Line(Blank + Blank + I(1) + I(451) + I(7) + I(0), mat, 1, 451);
        tape.Line(Blank + Blank + I(3) + I(1) + I(4) + I(0), mat, 1, 451);
        tape.Send(mat, 1);
        tape.Fend(mat);

        // MF=3, MT=1: one lin-lin region, numbers in compact Fortran form.
        tape.Cont(1001, 0.9991673, 0, 0, 0, 0, mat, 3, 1);
        tape.Cont(0, 0, 0, 0, 1, 3, mat, 3, 1);
        tape.Line(I(3) + I(2), mat, 3, 1);
        tape.Line(" 1.000000-5 3.700000+1 1.000000+6 4.000000+0 2.000000+7 5.000000-1", mat, 3, 1);
        tape.Send(mat, 3);

        // MF=3, MT=102: log-log up to point 2, lin-lin after.
        tape.Cont(1001, 0.9991673, 0, 0, 0, 0, mat, 3, 102);
        tape.Cont(2.224e6, 2.224e6, 0, 0, 2, 4, mat, 3, 102);
        tape.Line(I(2) + I(5) + I(4) + I(2), mat, 3, 102);
        tape.Line(R(1e-5) + R(10) + R(1e-3) + R(1) + R(1) + R(0.5), mat, 3, 102);
        tape.Line(R(3) + R(1.5), mat, 3, 102);
        tape.Send(mat, 3);
        tape.Fend(mat);

        // MF=4, MT=2: present but not parsed.
        tape.Cont(1001, 0.9991673, 0, 1, 0, 0, mat, 4, 2);
        tape.Cont(0, 0.9991673, 0, 2, 0, 0, mat, 4, 2);
        tape.Send(mat, 4);
        tape.Fend(mat);

        tape.Mend();
        tape.Tend();
        return tape.ToString();
    }
}
