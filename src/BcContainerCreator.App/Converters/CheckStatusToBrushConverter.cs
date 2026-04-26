using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BcContainerCreator.Core.Models;

namespace BcContainerCreator.App.Converters;

public sealed class CheckStatusToBrushConverter : IValueConverter
{
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4F)) { Opacity = 1 };
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xA8, 0x00));
    private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0x24, 0x31));
    private static readonly Brush PendingBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    static CheckStatusToBrushConverter()
    {
        OkBrush.Freeze();
        WarnBrush.Freeze();
        FailBrush.Freeze();
        PendingBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus s
            ? s switch
            {
                CheckStatus.Ok => OkBrush,
                CheckStatus.Warning => WarnBrush,
                CheckStatus.Failed => FailBrush,
                _ => PendingBrush
            }
            : PendingBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
