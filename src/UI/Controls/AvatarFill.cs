using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;

namespace Labs626.UrScore.UI;

/// <summary>
/// A cached picture file beside one of your accounts, as a round fill (plan A25). Never throws: a file that is gone,
/// half-written or not a picture gives no brush, and the row shows what it showed before the picture arrived.
/// <para>
/// Brushes are kept by path and write time, so redrawing a board every three minutes doesn't decode the same twenty
/// pictures again. The key is the path and its write time together, so a picture fetched again after its seven days
/// adds an entry rather than replacing one: one per account per refresh, which over a session is your account list.
/// </para>
/// </summary>
public sealed class AvatarFill : IValueConverter
{
    private readonly Dictionary<string, ImageBrush?> _brushes = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string file || file.Length == 0) return null;

        string key;
        try
        {
            key = $"{file}|{File.GetLastWriteTimeUtc(file).Ticks}";
        }
        catch (Exception)
        {
            return null;
        }

        if (_brushes.TryGetValue(key, out var known)) return known;

        // A picture that doesn't decode costs the picture. The row keeps its space and its name.
        ImageBrush? brush = null;
        if (PictureFile.Decode(file) is { } image)
        {
            brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
        }

        _brushes[key] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
