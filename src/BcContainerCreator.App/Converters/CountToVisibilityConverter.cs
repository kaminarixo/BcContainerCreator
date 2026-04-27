using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BcContainerCreator.App.Converters;

/// <summary>
/// int-Count → Visibility. Wenn parameter "invert" ist, wird die Logik
/// umgedreht (Count==0 → Visible).
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int n ? n : 0;
        var hasItems = count > 0;
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            hasItems = !hasItems;
        }
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
