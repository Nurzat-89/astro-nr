using AstroNucleaus.Domain.Endf;

namespace AstroNucleaus.Domain.Tests.Endf;

public class EndfMaterialTests
{
    private static EndfMaterial Create(double za, string? zsymam = null) =>
        new(9228, za, 233.0248,
            zsymam is null ? null : new EndfMaterialDescription { SymbolAndMass = zsymam },
            [], []);

    [Fact]
    public void Name_RemovesBlanksFromZsymam()
    {
        Assert.Equal("92-U-235", Create(92235, " 92-U -235").Name);
        Assert.Equal("1-H-1", Create(1001, "  1-H -  1").Name);
    }

    [Fact]
    public void Name_IsBuiltFromZaWithoutDescription()
    {
        Assert.Equal("26-Fe-56", Create(26056).Name);
        Assert.Equal("6-C-nat", Create(6000).Name);
        Assert.Equal("MAT 9228", Create(0).Name);
    }

    [Fact]
    public void ToString_IncludesMat()
    {
        Assert.Equal("92-U-235 (MAT 9228)", Create(92235, " 92-U -235").ToString());
    }

    [Theory]
    [InlineData(0, "n")]
    [InlineData(1, "H")]
    [InlineData(26, "Fe")]
    [InlineData(92, "U")]
    [InlineData(118, "Og")]
    [InlineData(119, null)]
    [InlineData(-1, null)]
    public void ChemicalElements_GetSymbol(int z, string? expected)
    {
        Assert.Equal(expected, ChemicalElements.GetSymbol(z));
    }

    [Fact]
    public void SectionInfo_DescribesFileAndReaction()
    {
        var section = new EndfSectionInfo(3, 102, 50);

        Assert.Equal("Reaction cross sections", section.FileType);
        Assert.Equal("(z,γ)", section.ReactionName);
        Assert.Equal(string.Empty, new EndfSectionInfo(1, 451, 10).ReactionName);
    }
}
