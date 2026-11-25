using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AstroNucleaus.Wpf.Converters;

/// <summary>
/// Visible when the value is "present": true, non-null, a non-empty string or collection, a non-zero number,
/// or equal to ConverterParameter (compared as text, e.g. an enum name).
/// </summary>
public sealed class VisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = parameter is string expected
            ? string.Equals(value?.ToString(), expected, StringComparison.Ordinal)
            : value switch
            {
                null => false,
                bool b => b,
                string s => s.Length > 0,
                int i => i != 0,
                ICollection c => c.Count > 0,
                _ => true,
            };

        return visible ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
