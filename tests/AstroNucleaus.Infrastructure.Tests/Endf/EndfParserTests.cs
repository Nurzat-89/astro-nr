using AstroNucleaus.Domain.Endf;
using AstroNucleaus.Infrastructure.Endf.Parsing;

namespace AstroNucleaus.Infrastructure.Tests.Endf;

public class EndfParserTests
{
    private static EndfTape ParseSample() =>
        new EndfParser().Parse(new StringReader(EndfTestTape.CreateSample()));

    [Fact]
    public void Parse_ReadsTapeIdentificationAndMaterial()
    {
        var tape = ParseSample();

        Assert.Equal("Test tape for AstroNucleaus", tape.Identification);
        Assert.Equal(1, tape.TapeNumber);
        Assert.Empty(tape.Warnings);

        var material = Assert.Single(tape.Materials);
        Assert.Equal(125, material.Mat);
        Assert.Equal(1001, material.ZA);
        Assert.Equal(1, material.Z);
        Assert.Equal(1, material.MassNumber);
        Assert.Equal(0.9991673, material.AtomicWeightRatio, 10);
    }

    [Fact]
    public void Parse_ReadsGeneralInformation()
    {
        var description = Assert.Single(ParseSample().Materials).Description;

        Assert.NotNull(description);
        Assert.Equal(6, description.FormatVersion);
        Assert.Equal(10, description.SubLibraryNumber);
        Assert.Equal("Incident neutron", description.SubLibraryName);
        Assert.Equal(8, description.LibraryVersion);
        Assert.Equal("ENDF/B", description.LibraryName);
        Assert.Equal(2.0e7, description.MaxEnergy);
        Assert.Equal(1.0, description.ProjectileMassRatio);
        Assert.True(description.IsStable);
        Assert.Equal("1-H -  1", description.SymbolAndMass);
        Assert.Equal("LANL", description.Laboratory);
        Assert.Equal("EVAL-JUL16", description.EvaluationDate);
        Assert.Equal("G.M.Hale", description.Authors);
        Assert.Equal("Ref. 1", description.Reference);
        Assert.Equal("DIST-FEB18", description.DistributionDate);
        Assert.Equal("REV1-JAN17", description.RevisionDate);
        Assert.Equal("20180206", description.MasterFileDate);
        Assert.Equal(3, description.Comments.Count);
        Assert.Equal(" Sample comment line", description.Comments[2]);
    }

    [Fact]
    public void Parse_ReadsCrossSectionsWithFortranNumbers()
    {
        var material = Assert.Single(ParseSample().Materials);

        Assert.Equal([1, 102], material.CrossSections.Select(x => x.MT));

        var total = material.GetCrossSection(ReactionTypes.Total)!;
        Assert.Equal([1e-5, 1e6, 2e7], total.Data.X);
        Assert.Equal([37.0, 4.0, 0.5], total.Data.Y);
        Assert.Equal(new InterpolationRegion(3, InterpolationLaw.LinLin), Assert.Single(total.Data.Regions));
        Assert.Equal("(z,total)", total.ReactionName);
    }

    [Fact]
    public void Parse_ReadsQValuesAndInterpolationRegions()
    {
        var capture = Assert.Single(ParseSample().Materials).GetCrossSection(ReactionTypes.RadiativeCapture)!;

        Assert.Equal(2.224e6, capture.MassDifferenceQValue);
        Assert.Equal(2.224e6, capture.ReactionQValue);
        Assert.Equal(4, capture.Data.Count);
        Assert.Equal(
            [new InterpolationRegion(2, InterpolationLaw.LogLog), new InterpolationRegion(4, InterpolationLaw.LinLin)],
            capture.Data.Regions);

        // 1/v-like log-log segment between (1e-5, 10) and (1e-3, 1).
        Assert.Equal(Math.Sqrt(10), capture.Data.Evaluate(1e-4), 10);
        // Lin-lin segment between (1, 0.5) and (3, 1.5).
        Assert.Equal(1.0, capture.Data.Evaluate(2), 12);
    }

    [Fact]
    public void Parse_ListsAllSections()
    {
        var material = Assert.Single(ParseSample().Materials);

        Assert.Equal(
            [(1, 451), (3, 1), (3, 102), (4, 2)],
            material.Sections.Select(s => (s.MF, s.MT)));
        Assert.Equal(9, material.Sections[0].LineCount);
    }

    [Fact]
    public void Parse_ReportsBrokenSectionAsWarningAndKeepsTheRest()
    {
        var text = EndfTestTape.CreateSample().Replace(" 1.000000+6", " 1.0000x0+6");

        var tape = new EndfParser().Parse(new StringReader(text));

        var warning = Assert.Single(tape.Warnings);
        Assert.Contains("MF 3, MT 1", warning);
        var material = Assert.Single(tape.Materials);
        Assert.Equal([102], material.CrossSections.Select(x => x.MT));
    }

    [Fact]
    public void Parse_HandlesMultipleMaterialsWithoutTpid()
    {
        var tape = new EndfTestTape();
        foreach (var mat in new[] { 2625, 2631 })
        {
            tape.Cont(26056, 55.454, 0, 0, 0, 0, mat, 3, 2);
            tape.Cont(0, 0, 0, 0, 1, 2, mat, 3, 2);
            tape.Line(EndfTestTape.I(2) + EndfTestTape.I(2), mat, 3, 2);
            tape.Line(EndfTestTape.R(1e-5) + EndfTestTape.R(10) + EndfTestTape.R(2e7) + EndfTestTape.R(1), mat, 3, 2);
            tape.Send(mat, 3);
            tape.Fend(mat);
            tape.Mend();
        }

        tape.Tend();

        var result = new EndfParser().Parse(new StringReader(tape.ToString()));

        Assert.Equal(string.Empty, result.Identification);
        Assert.Equal([2625, 2631], result.Materials.Select(m => m.Mat));
        Assert.All(result.Materials, m => Assert.Equal(26, m.Z));
        Assert.All(result.Materials, m => Assert.Null(m.Description));
    }

    [Fact]
    public void ParseFile_ReadsFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"astronucleaus-{Guid.NewGuid():N}.endf");
        File.WriteAllText(path, EndfTestTape.CreateSample());
        try
        {
            var tape = new EndfParser().ParseFile(path);
            Assert.Single(tape.Materials);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
