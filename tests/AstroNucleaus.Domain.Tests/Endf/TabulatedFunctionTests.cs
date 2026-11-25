using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Domain.Tests.Endf;

public class TabulatedFunctionTests
{
    private static TabulatedFunction Create(double[] x, double[] y, params (int LastPoint, InterpolationLaw Law)[] regions) =>
        new(x, y, regions.Select(r => new InterpolationRegion(r.LastPoint, r.Law)).ToArray());

    [Fact]
    public void Evaluate_LinLin()
    {
        var f = Create([1, 3], [2, 6], (2, InterpolationLaw.LinLin));
        Assert.Equal(4.0, f.Evaluate(2), 12);
    }

    [Fact]
    public void Evaluate_Histogram_UsesLeftValue()
    {
        var f = Create([1, 3], [2, 6], (2, InterpolationLaw.Histogram));
        Assert.Equal(2.0, f.Evaluate(2.9));
    }

    [Fact]
    public void Evaluate_LinLog()
    {
        var f = Create([1, 100], [0, 2], (2, InterpolationLaw.LinLog));
        Assert.Equal(1.0, f.Evaluate(10), 12);
    }

    [Fact]
    public void Evaluate_LogLin()
    {
        var f = Create([0, 2], [1, 100], (2, InterpolationLaw.LogLin));
        Assert.Equal(10.0, f.Evaluate(1), 10);
    }

    [Fact]
    public void Evaluate_LogLog()
    {
        var f = Create([1, 100], [1, 10000], (2, InterpolationLaw.LogLog));
        Assert.Equal(100.0, f.Evaluate(10), 9);
    }

    [Fact]
    public void Evaluate_LogLawWithZeroFallsBackToLinLin()
    {
        var f = Create([1, 3], [0, 4], (2, InterpolationLaw.LogLog));
        Assert.Equal(2.0, f.Evaluate(2), 12);
    }

    [Fact]
    public void Evaluate_UsesTheRegionOfEachInterval()
    {
        // Points 1-2 lin-lin, points 2-3 log-lin.
        var f = Create([1, 2, 3], [1, 2, 4], (2, InterpolationLaw.LinLin), (3, InterpolationLaw.LogLin));

        Assert.Equal(1.5, f.Evaluate(1.5), 12);
        Assert.Equal(2 * Math.Sqrt(2), f.Evaluate(2.5), 12);
    }

    [Fact]
    public void Evaluate_ReturnsZeroOutsideRange()
    {
        var f = Create([1, 2], [5, 5], (2, InterpolationLaw.LinLin));

        Assert.Equal(0.0, f.Evaluate(0.5));
        Assert.Equal(0.0, f.Evaluate(2.5));
        Assert.Equal(0.0, f.Evaluate(double.NaN));
    }

    [Fact]
    public void Evaluate_AtTabulatedPoint_ReturnsPointValue()
    {
        var f = Create([1, 2, 3], [1, 7, 4], (3, InterpolationLaw.LogLog));

        Assert.Equal(1.0, f.Evaluate(1));
        Assert.Equal(7.0, f.Evaluate(2));
        Assert.Equal(4.0, f.Evaluate(3));
    }

    [Fact]
    public void Evaluate_AtDiscontinuity_ReturnsRightValue()
    {
        var f = Create([1, 2, 2, 3], [1, 1, 5, 5], (4, InterpolationLaw.LinLin));

        Assert.Equal(5.0, f.Evaluate(2));
        Assert.Equal(1.0, f.Evaluate(1.5));
    }

    [Fact]
    public void Constructor_RejectsDecreasingX()
    {
        Assert.Throws<ArgumentException>(() => Create([2, 1], [1, 1], (2, InterpolationLaw.LinLin)));
    }

    [Fact]
    public void Constructor_RejectsLengthMismatch()
    {
        Assert.Throws<ArgumentException>(() => Create([1, 2], [1], (2, InterpolationLaw.LinLin)));
    }

    [Theory]
    [InlineData(1, "(z,total)")]
    [InlineData(102, "(z,γ)")]
    [InlineData(53, "(z,n3) level 3")]
    [InlineData(803, "(z,α3) level 3")]
    [InlineData(9999, "MT 9999")]
    public void ReactionTypes_GetName(int mt, string expected)
    {
        Assert.Equal(expected, ReactionTypes.GetName(mt));
    }
}
