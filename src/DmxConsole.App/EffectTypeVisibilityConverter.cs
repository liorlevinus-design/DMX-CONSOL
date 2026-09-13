using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DmxConsole.App;

/// <summary>
/// Shows an element only when the bound effect-type string is one of a comma-separated
/// list passed as ConverterParameter (e.g. "Chase,Strobe,Sine") - used to reveal only the
/// parameter fields relevant to the currently selected effect type.
/// </summary>
public sealed class EffectTypeVisibilityConverter : IValueConverter
{
    public static readonly EffectTypeVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var current = value as string ?? string.Empty;
        var allowed = (parameter as string ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Contains(current, StringComparer.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
