using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The tray wears the main clan's icon (ported from K0ii Score with tray mode): the same picture the window and the taskbar
/// wear (<c>AppServices.WindowIcon</c>), made into a tray icon here, and Ur Score's own icon before the first one is fetched
/// or when a picture won't decode. No test builds the tray itself (it is a shell icon);
/// this is the picture it is handed, and the wiring that hands it.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class TrayPictureTests : IDisposable
{
    private readonly TempDir.Scope _dir = TempDir.Create("urscore-tray-picture");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void AClanPictureBecomesATrayIconAtTheTraysSize()
    {
        var file = Path.Combine(_dir.Path, "12345.png");
        File.WriteAllBytes(file, Png(150));

        using var icon = TrayPicture.From(file);

        Assert.NotNull(icon);
        Assert.Equal(TrayPicture.Size, icon.Width);
        Assert.Equal(TrayPicture.Size, icon.Height);
    }

    /// <summary>Review, 2026-09-27: a picture that isn't square is cropped to its centre, never stretched.</summary>
    [Fact]
    public void APictureThatIsNotSquareIsCroppedToItsCentre()
    {
        var wide = Path.Combine(_dir.Path, "wide.png");
        File.WriteAllBytes(wide, Bands(150, 50, Red, Green, Blue));
        var tall = Path.Combine(_dir.Path, "tall.png");
        File.WriteAllBytes(tall, Bands(50, 150, Red, Green, Blue, across: false));

        foreach (var file in new[] { wide, tall })
        {
            using var icon = TrayPicture.From(file);
            using var bitmap = icon!.ToBitmap();

            Assert.Equal(TrayPicture.Size, bitmap.Width);
            foreach (var (x, y) in new[] { (1, 1), (TrayPicture.Size / 2, TrayPicture.Size / 2), (TrayPicture.Size - 2, TrayPicture.Size - 2) })
            {
                var pixel = bitmap.GetPixel(x, y);
                Assert.True(pixel.G > 200 && pixel.R < 60 && pixel.B < 60, $"{Path.GetFileName(file)} at ({x},{y}) is {pixel}, not the centre's green");
            }
        }
    }

    private const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF;

    /// <summary>A picture in three equal bands, left to right (or top to bottom when not <paramref name="across"/>).</summary>
    private static byte[] Bands(int width, int height, uint first, uint second, uint third, bool across = true)
    {
        byte[] bytes = [];
        UiThread.Run(() =>
        {
            var pixels = new uint[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var band = across ? x * 3 / width : y * 3 / height;
                    pixels[y * width + x] = band == 0 ? first : band == 1 ? second : third;
                }
            }

            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            bytes = stream.ToArray();
        });
        return bytes;
    }

    [Fact]
    public void NoPictureOrOneThatWontDecodeIsNone()
    {
        var broken = Path.Combine(_dir.Path, "broken.png");
        File.WriteAllBytes(broken, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

        Assert.Null(TrayPicture.From(null));
        Assert.Null(TrayPicture.From(""));
        Assert.Null(TrayPicture.From(Path.Combine(_dir.Path, "missing.png")));
        Assert.Null(TrayPicture.From(broken));
    }

    /// <summary>
    /// The app hands the tray the window's icon when it builds it, and every icon after: the tray follows the same clan icon
    /// the window and the taskbar do, and falls back to its own when there is none.
    /// </summary>
    [Fact]
    public void TheAppHandsTheTrayTheWindowsIconAtStartAndOnEveryChange()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "App.xaml.cs"));
        var tray = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Tray.cs"));

        Assert.Contains("_tray.ShowPicture(services.WindowIcon.File);", app, StringComparison.Ordinal);
        Assert.Contains("services.IconChanged += icon => _tray?.ShowPicture(icon.File);", app, StringComparison.Ordinal);
        Assert.Contains("TrayPicture.From(file)", tray, StringComparison.Ordinal);
    }

    private static byte[] Png(int size)
    {
        byte[] bytes = [];
        UiThread.Run(() =>
        {
            var pixels = new uint[size * size];
            Array.Fill(pixels, 0xFF7B2FBE);
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, size * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            bytes = stream.ToArray();
        });
        return bytes;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj"))) dir = dir.Parent;

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
