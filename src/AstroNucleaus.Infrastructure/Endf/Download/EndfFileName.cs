using System.Globalization;
using System.Text.RegularExpressions;
using AstroNucleaus.Application.Endf;

namespace AstroNucleaus.Infrastructure.Endf.Download;

/// <summary>
/// Interprets the evaluation file names used by the IAEA and NNDC.
/// </summary>
internal static partial class EndfFileName
{
    /// <summary>
    /// IAEA standalone files: {projectile}_{Z}-{symbol}-{A}{isomer}_{MAT}.zip, e.g. "n_026-Fe-56_2631.zip".
    /// </summary>
    public static EndfRemoteFile Parse(string library, string sublibrary, string fileName, Uri uri)
    {
        var file = new EndfRemoteFile(library, sublibrary, fileName, uri);
        var match = IaeaRegex().Match(fileName);
        return match.Success ? WithNuclide(file, match) : file;
    }

    /// <summary>
    /// NNDC archive entries: {projectile}-{Z}_{symbol}_{A}{isomer}.endf, e.g. "ENDF-B-VIII.0_neutrons/n-013_Al_026m1.endf".
    /// </summary>
    public static EndfRemoteFile ParseArchiveEntry(string library, string sublibrary, Uri archiveUri, string entryName)
    {
        var fileName = entryName[(entryName.LastIndexOf('/') + 1)..];
        var file = new EndfRemoteFile(library, sublibrary, fileName, archiveUri) { ArchiveEntry = entryName };
        var match = NndcRegex().Match(fileName);
        return match.Success ? WithNuclide(file, match) : file;
    }

    private static EndfRemoteFile WithNuclide(EndfRemoteFile file, Match match) => file with
    {
        Projectile = match.Groups["proj"].Value,
        Z = int.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture),
        Symbol = match.Groups["sym"].Value,
        MassNumber = match.Groups["a"].Value.TrimStart('0') is { Length: > 0 } a ? a : "0",
        Isomer = match.Groups["iso"].Success ? match.Groups["iso"].Value : null,
        Mat = match.Groups["mat"].Success ? int.Parse(match.Groups["mat"].Value, CultureInfo.InvariantCulture) : null,
    };

    [GeneratedRegex(
        @"^(?<proj>[A-Za-z0-9]+)_(?<z>\d{1,3})-(?<sym>[A-Za-z]{1,3})-(?<a>\d{1,3})(?<iso>[MmNn]\d*)?(?:_(?<mat>\d{1,4}))?\.zip$")]
    private static partial Regex IaeaRegex();

    [GeneratedRegex(
        @"^(?<proj>[A-Za-z0-9]+)-(?<z>\d{1,3})_(?<sym>[A-Za-z]{1,3})_(?<a>\d{1,3})(?<iso>[MmNn]\d*)?\.[A-Za-z0-9]+$")]
    private static partial Regex NndcRegex();
}
