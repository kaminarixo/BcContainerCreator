using System.Globalization;
using System.Windows.Data;

namespace BcContainerCreator.App.Converters;

/// <summary>
/// Generischer Equals-Converter — vergleicht ein gebundenes Objekt mit dem
/// ConverterParameter via .Equals/.ToString. Liefert bool. Nützlich für
/// segmented-Controls, in denen ein Enum/String-Wert die aktive Option steuert.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null && parameter is null)
        {
            return true;
        }
        if (value is null || parameter is null)
        {
            return false;
        }
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null)
        {
            return parameter;
        }
        return Binding.DoNothing;
    }
}
