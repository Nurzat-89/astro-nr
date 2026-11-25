using AstroNucleaus.Infrastructure.Endf.Parsing;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

public class EndfNumberTests
{
    [Theory]
    [InlineData(" 1.001000+3", 1001.0)]
    [InlineData("-2.500000-1", -0.25)]
    [InlineData(" 9.991673-1", 0.9991673)]
    [InlineData("-1.234567+10", -1.234567e10)]
    [InlineData(" 1.0000-11 ", 1.0e-11)]
    [InlineData("1.0E+06", 1.0e6)]
    [InlineData(" 2.0D+02", 200.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("-7", -7.0)]
    [InlineData("+3.0+2", 300.0)]
    [InlineData("           ", 0.0)]
    [InlineData(" 1.0000 +5", 1.0e5)]
    public void ParseReal_AcceptsEndfNotations(string field, double expected)
    {
        Assert.Equal(expected, EndfNumber.ParseReal(field), 12);
    }

    [Fact]
    public void ParseReal_RejectsGarbage()
    {
        Assert.Throws<FormatException>(() => EndfNumber.ParseReal("abc"));
    }

    [Theory]
    [InlineData("        125", 125)]
    [InlineData("         -1", -1)]
    [InlineData("           ", 0)]
    [InlineData(" 1.000000+0", 1)]
    public void ParseInt_AcceptsIntegerFields(string field, int expected)
    {
        Assert.Equal(expected, EndfNumber.ParseInt(field));
    }
}
