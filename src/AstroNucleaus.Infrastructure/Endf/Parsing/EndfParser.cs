using System.Text;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Domain.Endf;
using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Infrastructure.Endf.Parsing;

/// <summary>
/// ENDF-6 parser. Splits a tape into materials and sections (MAT/MF/MT), parses
/// the general information (MF=1, MT=451) and cross sections (MF=3), and lists every other section.
/// </summary>
public sealed class EndfParser : IEndfParser
{
    public EndfTape ParseFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public EndfTape Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var identification = string.Empty;
        var tapeNumber = 0;
        var materials = new List<EndfMaterial>();
        var warnings = new List<string>();

        MaterialBuilder? material = null;
        List<EndfLine>? sectionLines = null;
        var isFirstLine = true;
        var lineNumber = 0;

        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var line = EndfLine.Parse(raw, lineNumber);

            if (isFirstLine)
            {
                isFirstLine = false;
                if (line is { MF: 0, MT: 0 } && line.Mat >= 0)
                {
                    // TPID record: tape identification, MAT field holds the tape number.
                    identification = line.Data.Trim();
                    tapeNumber = line.Mat;
                    continue;
                }
            }

            if (line.Mat == -1)
                break; // TEND

            if (line.Mat == 0)
            {
                // MEND: end of material.
                FlushSection();
                FlushMaterial();
                continue;
            }

            if (line.MF == 0 || line.MT == 0)
            {
                // FEND (end of file) or SEND (end of section).
                FlushSection();
                continue;
            }

            if (material is not null && material.Mat != line.Mat)
            {
                // Missing MEND: start a new material anyway.
                FlushSection();
                FlushMaterial();
            }

            material ??= new MaterialBuilder(line.Mat, warnings);

            if (sectionLines is { Count: > 0 } && (sectionLines[0].MF != line.MF || sectionLines[0].MT != line.MT))
                FlushSection(); // missing SEND

            sectionLines ??= [];
            sectionLines.Add(line);
        }

        FlushSection();
        FlushMaterial();

        return new EndfTape(identification, tapeNumber, materials, warnings);

        void FlushSection()
        {
            if (sectionLines is { Count: > 0 } && material is not null)
                material.AddSection(sectionLines);
            sectionLines = null;
        }

        void FlushMaterial()
        {
            if (material is not null)
                materials.Add(material.Build());
            material = null;
        }
    }

    private sealed class MaterialBuilder
    {
        private readonly List<string> _warnings;
        private readonly List<CrossSection> _crossSections = [];
        private readonly List<EndfSectionInfo> _sections = [];
        private EndfMaterialDescription? _description;
        private RadioactiveDecay? _decay;
        private double? _za;
        private double? _awr;

        public MaterialBuilder(int mat, List<string> warnings)
        {
            Mat = mat;
            _warnings = warnings;
        }

        public int Mat { get; }

        public void AddSection(IReadOnlyList<EndfLine> lines)
        {
            var mf = lines[0].MF;
            var mt = lines[0].MT;
            _sections.Add(new EndfSectionInfo(mf, mt, lines.Count));

            try
            {
                var reader = new EndfRecordReader(lines);
                switch (mf, mt)
                {
                    case (1, 451):
                        _description = ReadGeneralInformation(reader);
                        break;
                    case (3, _):
                        _crossSections.Add(ReadCrossSection(reader, mt));
                        break;
                    case (8, 457):
                        _decay = ReadRadioactiveDecay(reader);
                        break;
                    default:
                        // Remember ZA/AWR from the HEAD record of any section.
                        SetZaAwr(reader.ReadCont());
                        break;
                }
            }
            catch (EndfFormatException ex)
            {
                _warnings.Add($"MAT {Mat}, MF {mf}, MT {mt}: {ex.Message}");
            }
        }

        public EndfMaterial Build() =>
            new(Mat, _za ?? 0, _awr ?? 0, _description, _crossSections, _sections, _decay);

        private RadioactiveDecay ReadRadioactiveDecay(EndfRecordReader reader)
        {
            // HEAD: ZA, AWR, LIS, LISO, NST, NSP
            var head = reader.ReadCont();
            SetZaAwr(head);

            // LIST: T½, ΔT½, 0, 0, NC, 0 / average decay energies
            var (halfLifeRecord, _) = reader.ReadList();

            // LIST: SPI, PAR, 0, 0, 6·NDK, NDK / RTYP, RFS, Q, ΔQ, BR, ΔBR for each decay mode
            var (_, modes) = reader.ReadList();

            var halfLife = halfLifeRecord.C1;
            if (head.N1 == 1 || halfLife <= 0)
                return RadioactiveDecay.Stable;

            var branches = new List<DecayBranch>();
            for (var k = 0; k + 5 < modes.Length; k += 6)
            {
                var rtyp = modes[k];
                var fraction = modes[k + 4];
                if (rtyp > 0 && fraction > 0)
                    branches.Add(RadioactiveDecay.BranchFromRtyp(rtyp, fraction));
            }

            return new RadioactiveDecay(halfLife, branches);
        }

        private void SetZaAwr(ContRecord head)
        {
            _za ??= head.C1;
            _awr ??= head.C2;
        }

        private CrossSection ReadCrossSection(EndfRecordReader reader, int mt)
        {
            SetZaAwr(reader.ReadCont());               // HEAD: ZA, AWR, 0, 0, 0, 0
            var (tab, function) = reader.ReadTab1();   // TAB1: QM, QI, 0, LR, NR, NP
            return new CrossSection(mt, tab.C1, tab.C2, tab.L2, function);
        }

        private EndfMaterialDescription ReadGeneralInformation(EndfRecordReader reader)
        {
            // HEAD: ZA, AWR, LRP, LFI, NLIB, NMOD
            var head = reader.ReadCont();
            _za = head.C1;
            _awr = head.C2;

            var second = reader.ReadCont();
            var description = new EndfMaterialDescription
            {
                ResonanceParameterFlag = head.L1,
                IsFissionable = head.L2 == 1,
                LibraryNumber = head.N1,
                ModificationNumber = head.N2,
            };

            int textLineCount;
            if (second.N2 >= 6)
            {
                // ENDF-6: [ELIS, STA, LIS, LISO, 0, NFOR] [AWI, EMAX, LREL, 0, NSUB, NVER] [TEMP, 0, LDRV, 0, NWD, NXC]
                var third = reader.ReadCont();
                var fourth = reader.ReadCont();
                description = description with
                {
                    ExcitationEnergy = second.C1,
                    IsStable = second.C2 == 0,
                    ExcitedStateNumber = second.L1,
                    IsomerNumber = second.L2,
                    FormatVersion = second.N2,
                    ProjectileMassRatio = third.C1,
                    MaxEnergy = third.C2,
                    ReleaseNumber = third.L1,
                    SubLibraryNumber = third.N1,
                    LibraryVersion = third.N2,
                    Temperature = fourth.C1,
                };
                textLineCount = fourth.N1;
            }
            else
            {
                // Older formats: [ELIS, STA, LDD, LFP, NWD, NXC]
                description = description with { ExcitationEnergy = second.C1, IsStable = second.C2 == 0, FormatVersion = 5 };
                textLineCount = second.N1;
            }

            var text = new string[textLineCount];
            for (var i = 0; i < textLineCount; i++)
                text[i] = reader.ReadText().TrimEnd();

            description = description with { Comments = text };

            if (description.FormatVersion >= 6 && text.Length >= 2)
            {
                // Line 1: ZSYMAM (1-11), ALAB (12-22), EDATE (23-32), AUTH (34-66)
                // Line 2: REF (2-22), DDATE (23-32), RDATE (34-43), ENDATE (56-63)
                var line1 = text[0].PadRight(EndfLine.DataWidth);
                var line2 = text[1].PadRight(EndfLine.DataWidth);
                description = description with
                {
                    SymbolAndMass = line1[..11].Trim(),
                    Laboratory = line1[11..22].Trim(),
                    EvaluationDate = line1[22..32].Trim(),
                    Authors = line1[33..66].Trim(),
                    Reference = line2[1..22].Trim(),
                    DistributionDate = line2[22..32].Trim(),
                    RevisionDate = line2[33..43].Trim(),
                    MasterFileDate = line2[55..63].Trim(),
                };
            }

            // The directory (NXC records) that follows is not needed: sections are listed from the data itself.
            return description;
        }
    }
}
