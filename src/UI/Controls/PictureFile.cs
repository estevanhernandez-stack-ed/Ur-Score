using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Labs626.UrScore.UI;

/// <summary>
/// A cached picture file as an image to draw: a clan's icon (backlog V3-S.7). Never throws: a file that is gone, half-written or
/// not a picture gives nothing, and its slot shows what it showed before the picture arrived.
/// <para>
/// Kept by path and write time, as <see cref="AvatarFill"/> keeps its brushes, so a board redrawn every few minutes doesn't
/// decode the same picture again, and a picture fetched again after its seven days is decoded once more.
/// </para>
/// </summary>
public sealed class PictureFile : IValueConverter
{
    private readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A picture slot's visibility (A26): shown with its picture; while a picture belongs there but isn't there yet or doesn't
    /// decode, laid out and empty, so nothing moves when it lands; and gone only when no picture belongs there at all.
    /// </summary>
    public static Visibility SlotVisibility(bool hasSlot, bool hasPicture) =>
        hasPicture ? Visibility.Visible : hasSlot ? Visibility.Hidden : Visibility.Collapsed;

    /// <summary>The picture in <paramref name="file"/>, decoded whole and frozen, or null for anything that isn't one.</summary>
    public static BitmapImage? Decode(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

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

        if (_images.TryGetValue(key, out var known)) return known;

        var image = Decode(file);
        _images[key] = image;
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
