using System.Xml.Linq;

namespace UrScore.Tests;

public class AccountsPageSpacingTests
{
    [Fact]
    public void IntroductoryNotesUseOneCompactGapAndKeepSpaceBeforeTheTable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var page = XDocument.Load(Path.Combine(directory.FullName, "src", "UI", "Setup", "AccountsPage.xaml"));
        var intro = page.Root!.Element(presentation + "StackPanel")!.Elements(presentation + "TextBlock").Take(4).ToArray();
        Assert.Equal(4, intro.Length);
        Assert.Equal("ListedLine", (string?)intro[1].Attribute(xaml + "Name"));
        Assert.Equal("0,4,0,4", (string?)intro[1].Attribute("Margin"));
        Assert.StartsWith("Send reports", (string?)intro[2].Attribute("Text"));
        Assert.Equal("0,0,0,4", (string?)intro[2].Attribute("Margin"));
        Assert.StartsWith("Each picture", (string?)intro[3].Attribute("Text"));
        Assert.Equal("0,0,0,12", (string?)intro[3].Attribute("Margin"));
        Assert.All(intro.Skip(1), element => Assert.Equal("{StaticResource Muted}", (string?)element.Attribute("Style")));
        var app = XDocument.Load(Path.Combine(directory.FullName, "src", "App.xaml"));
        var muted = app.Descendants(presentation + "Style").Single(element => (string?)element.Attribute(xaml + "Key") == "Muted");
        Assert.Contains(muted.Elements(presentation + "Setter"), element =>
            (string?)element.Attribute("Property") == "TextWrapping" && (string?)element.Attribute("Value") == "Wrap");
    }

    /// <summary>
    /// With two recipes, each taking a 160 px Send column, "Found in" is left about as wide as "No clan is in a battle right
    /// now", which then ran flush into the first Send box ("right now☑ Send"). The cell keeps a gap before the Send columns,
    /// and both of its lines wrap into it instead of reaching them. Found in the 0.3.4 how-to screenshots.
    /// </summary>
    [Fact]
    public void WhereAnAccountWasFoundWrapsBeforeTheSendColumns()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var page = XDocument.Load(Path.Combine(directory.FullName, "src", "UI", "Setup", "AccountsPage.xaml"));

        var foundIn = page.Descendants(presentation + "TextBlock").Single(element => (string?)element.Attribute("Text") == "{Binding FoundIn}");
        var cell = foundIn.Parent!;
        Assert.Equal("1", (string?)cell.Attribute("Grid.Column"));
        var margin = ((string?)cell.Attribute("Margin") ?? "0").Split(',').Select(double.Parse).ToArray();
        Assert.True(margin.Length == 4 && margin[2] >= 12, $"the Found in cell needs at least 12 px before the Send columns, has Margin='{cell.Attribute("Margin")}'");
        Assert.All(cell.Elements(presentation + "TextBlock"), line => Assert.Equal("{StaticResource Muted}", (string?)line.Attribute("Style")));
    }
}
