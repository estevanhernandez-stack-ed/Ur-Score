using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Labs626.UrScore.UI;

/// <summary>
/// A cached picture file beside one of your accounts, as a round fill (plan A25). Never throws: a file that is gone,
/// half-written or not a picture gives no brush, and the row shows what it showed before the picture arrived.
/// <para>
/// Brushes are kept by path and write time, so redrawing a board every three minutes doesn't decode the same twenty
/// pictures again. The map holds one entry per account, so it cannot grow past your account list.
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

        ImageBrush? brush = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();

            brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
        }
        catch (Exception)
        {
            // A picture that doesn't decode costs the picture. The row keeps its space and its name.
        }

        _brushes[key] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
