using System.Globalization;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>Time formatting shared by the animation views.</summary>
internal static class PlaybackFormatting
{
    private const double SecondsPerYear = 3.15576e7;

    public static string Seconds(double seconds) => seconds switch
    {
        < 1e-3 => $"{seconds * 1e6:0.#} µs",
        < 1 => $"{seconds * 1e3:0.#} ms",
        < 60 => $"{seconds:0.##} s",
        < 3600 => $"{seconds / 60:0.#} min",
        < 86400 => $"{seconds / 3600:0.#} h",
        < SecondsPerYear => $"{seconds / 86400:0.#} d",
        _ => Years(seconds / SecondsPerYear),
    };

    public static string Years(double years) => years switch
    {
        < 1 => $"{years * 365.25:0.#} d",
        < 1e3 => $"{years:0.#} yr",
        < 1e6 => $"{years / 1e3:0.##} kyr",
        < 1e9 => $"{years / 1e6:0.##} Myr",
        _ => $"{years / 1e9:0.##} Gyr",
    };

    public static string NeutronDensity(double value) =>
        value < 1 ? "≈ 0" : value.ToString("0.0E+0", CultureInfo.CurrentCulture);
}
