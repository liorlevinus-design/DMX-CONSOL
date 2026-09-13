using System.Globalization;
using System.Windows.Data;

namespace DmxConsole.App;

/// <summary>Simple !value converter for XAML bindings (e.g. "enabled while NOT running").</summary>
public sealed class BoolInverter : IValueConverter
{
    public static readonly BoolInverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}
