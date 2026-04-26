using System.Globalization;
using System.Windows.Data;
using BcContainerCreator.Core.Models;

namespace BcContainerCreator.App.Converters;

public sealed class CheckStatusToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus s
            ? s switch
            {
                CheckStatus.Ok => "OK",
                CheckStatus.Warning => "!",
                CheckStatus.Failed => "X",
                _ => "..."
            }
            : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
