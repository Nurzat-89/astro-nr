using System.Globalization;
using System.Windows.Data;

namespace AstroNucleaus.Wpf.Converters;

/// <summary>
/// Two-way converter between an enum value and "is it equal to ConverterParameter" (for radio buttons).
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name && string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
