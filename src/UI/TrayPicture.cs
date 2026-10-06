using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace Labs626.UrScore.UI;

/// <summary>
/// The clan's picture as a tray icon: the same file the window and the taskbar wear (<c>AppServices.WindowIcon</c>, the main clan's), drawn at
/// <see cref="Size"/> pixels (ported from K0ii Score). A picture that isn't square is cropped to its centre square, never stretched. The tray shows Ur Score's own icon until there is one, and again whenever one won't decode.
/// </summary>
public static class TrayPicture
{
    /// <summary>The notification area's icon at 200% scaling; Windows shrinks it for smaller ones.</summary>
    public const int Size = 32;

    /// <summary>An icon the caller owns and disposes, or null for no file, a missing one, or anything that isn't a picture.</summary>
    public static Icon? From(string? file)
    {
        if (string.IsNullOrEmpty(file)) return null;

        try
        {
            if (!File.Exists(file)) return null;

            // Read whole first, so the cache file is never held open by the bitmap.
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            using var source = new Bitmap(stream);
            using var small = new Bitmap(Size, Size);
            using (var canvas = Graphics.FromImage(small))
            {
                canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var side = Math.Min(source.Width, source.Height);
                var middle = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
                canvas.DrawImage(source, new Rectangle(0, 0, Size, Size), middle, GraphicsUnit.Pixel);
            }

            var handle = small.GetHicon();
            try
            {
                // FromHandle doesn't own the handle; its clone copies it into one that does, so the original can go now.
                using var borrowed = Icon.FromHandle(handle);
                return (Icon)borrowed.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
