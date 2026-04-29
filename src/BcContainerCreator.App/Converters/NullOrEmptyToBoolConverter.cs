using System.Globalization;
using System.Windows.Data;

namespace BcContainerCreator.App.Converters;

/// <summary>
/// Liefert <c>true</c>, wenn der Wert ein nicht-leerer String oder ein
/// nicht-null Objekt ist. Mit ConverterParameter "invert" wird das Ergebnis
/// invertiert. Geeignet für IsEnabled-Bindings auf Strings (z. B. WebClientUrl).
/// </summary>
public sealed class NullOrEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true,
        };

        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !hasValue : hasValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
