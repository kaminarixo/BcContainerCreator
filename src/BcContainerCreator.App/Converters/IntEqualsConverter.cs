using System.Globalization;
using System.Windows.Data;

namespace BcContainerCreator.App.Converters;

/// <summary>
/// Vergleicht einen Int-Wert (z. B. SelectedTabIndex) mit dem ConverterParameter
/// und liefert true, wenn gleich. Wird für RadioButton-IsChecked-Bindings im
/// Sidebar-Nav genutzt.
/// </summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        if (!int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        if (!int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
        {
            return false;
        }

        return v == p;
    }

    /// <summary>
    /// Bei IsChecked=true den ConverterParameter als neuen Index zurückgeben;
    /// false-Notifications (z. B. wenn ein anderes RadioButton aktiv wird)
    /// ignorieren wir mit <see cref="Binding.DoNothing"/>.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null
            && int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
        {
            return p;
        }
        return Binding.DoNothing;
    }
}
