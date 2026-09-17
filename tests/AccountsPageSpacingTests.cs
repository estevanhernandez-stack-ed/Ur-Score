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
}
