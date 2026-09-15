using System.Globalization;
using System.Windows.Data;

namespace Labs626.UrScore.UI;

/// <summary>Shows a panel title upper case, as the mock does (D21). The title's accessible name is bound to the original text.</summary>
public sealed class UpperCase : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpperInvariant() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
