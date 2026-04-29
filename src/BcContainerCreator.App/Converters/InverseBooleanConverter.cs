using System.Globalization;
using System.Windows.Data;

namespace BcContainerCreator.App.Converters;

/// <summary>
/// Invertiert einen Bool-Wert. Geeignet für IsEnabled-Bindings, bei denen
/// ein "Loading"- oder "Busy"-Flag den Button deaktivieren soll.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
