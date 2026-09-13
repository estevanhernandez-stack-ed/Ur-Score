using Labs626.UrScore.Theming;
using ROROROblox.PluginContract;

namespace UrScore.Tests;

public class HostPaletteTests
{
    private static ThemePalette Midnight() => new()
    {
        Bg = "#0A1320", Cyan = "#3FB8D9", Magenta = "#C0407E", White = "#E6EDF5", MutedText = "#6F7E92",
        Divider = "#162232", RowBg = "#0F1B2B", InteractiveEdge = "#5A6B80",
    };

    [Fact]
    public void EveryFallbackSlotIsAColour()
    {
        var brand = HostPalette.Brand;

        foreach (var hex in new[] { brand.Bg, brand.Cyan, brand.Magenta, brand.White, brand.MutedText, brand.Divider, brand.RowBg, brand.Edge, brand.RowHover })
        {
            Assert.True(HostPalette.IsColour(hex), $"{hex} is not #RRGGBB");
        }
    }

    [Fact]
    public void RoRoRosPaletteIsTakenSlotForSlot()
    {
        var palette = HostPalette.Brand.Merge(Midnight());

        Assert.Equal(new HostPalette("#0A1320", "#3FB8D9", "#C0407E", "#E6EDF5", "#6F7E92", "#162232", "#0F1B2B", "#5A6B80"), palette);
    }

    [Fact]
    public void ASlotThatIsNotAColourKeepsWhatWasOnScreen()
    {
        var incoming = Midnight();
        incoming.Cyan = "not a colour";
        incoming.InteractiveEdge = "";

        var palette = HostPalette.Brand.Merge(incoming);

        Assert.Equal(HostPalette.Brand.Cyan, palette.Cyan);
        Assert.Equal(HostPalette.Brand.Edge, palette.Edge);
        Assert.Equal("#0A1320", palette.Bg);
    }

    [Fact]
    public void HoverTintsTheRowTowardTheTextColour()
    {
        var palette = new HostPalette("#000000", "#17D4FA", "#F22F89", "#FFFFFF", "#9AA8B8", "#1F3149", "#000000", "#6B7F92");

        // 4% of the way from black to white: 255 * 0.04 = 10.2, rounded to 10 (0A).
        Assert.Equal("#0A0A0A", palette.RowHover);
    }

    [Fact]
    public void ALightThemeIsNotDark()
    {
        Assert.True(HostPalette.Brand.IsDark);
        Assert.False((HostPalette.Brand with { Bg = "#F4F6F8" }).IsDark);
    }

    [Fact]
    public void ATitleBarColourIsWindowsByteOrder()
    {
        Assert.Equal(0x00311F0Fu, HostPalette.ToColorRef("#0F1F31"));
    }
}
