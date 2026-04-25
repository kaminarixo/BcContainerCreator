using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Serilog.Events;

namespace BcContainerLauncher.App.Converters;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly Brush Verbose = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly Brush Debug = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly Brush Info = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xE6, 0xA8, 0x00));
    private static readonly Brush Error = new SolidColorBrush(Color.FromRgb(0xCB, 0x24, 0x31));
    private static readonly Brush Fatal = new SolidColorBrush(Color.FromRgb(0x80, 0x00, 0x80));

    static LogLevelToBrushConverter()
    {
        Verbose.Freeze(); Debug.Freeze(); Info.Freeze();
        Warn.Freeze(); Error.Freeze(); Fatal.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LogEventLevel l
            ? l switch
            {
                LogEventLevel.Verbose => Verbose,
                LogEventLevel.Debug => Debug,
                LogEventLevel.Information => Info,
                LogEventLevel.Warning => Warn,
                LogEventLevel.Error => Error,
                LogEventLevel.Fatal => Fatal,
                _ => Info
            }
            : Info;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
