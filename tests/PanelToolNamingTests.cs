using System.Text.RegularExpressions;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// A panel's own tools say which panel they belong to (owner's look, 2026-09-16). Five panels on a board gave a
/// screen reader five buttons all called "Pop out" and five all called "Panel settings", and an automation caller
/// had to reach past the name to the panel's id to press the right one. Each is now named from the panel's own
/// title, as "Add {title}", "Bring back {title}" and "Remove {title}" already are.
/// </summary>
public class PanelToolNamingTests
{
    [Fact]
    public void EachPanelsToolsSayWhichPanelTheyBelongTo()
    {
        Assert.Equal("Pop out Past battles", BoardText.PopOutName("Past battles"));
        Assert.Equal("Settings for Past battles", BoardText.PanelSettingsName("Past battles"));
        Assert.Equal("Choose another for Past battles", BoardText.ChooseAnotherName("Past battles"));
    }

    [Fact]
    public void NoTwoPanelsToolsAnswerToTheSameName()
    {
        // The defect itself: on a board of five panels every ⧉ answered to one name and every ⋯ to another.
        string[] titles = ["Clan standing", "Past battles", "Records", "My accounts", "Your accounts side by side"];

        var popOuts = titles.Select(BoardText.PopOutName).ToList();
        var settings = titles.Select(BoardText.PanelSettingsName).ToList();
        var chooseAnother = titles.Select(BoardText.ChooseAnotherName).ToList();

        Assert.Equal(titles.Length, popOuts.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(titles.Length, settings.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(titles.Length, chooseAnother.Distinct(StringComparer.Ordinal).Count());

        // And each still says what pressing it does, not only which panel it belongs to.
        Assert.All(popOuts, name => Assert.StartsWith("Pop out ", name, StringComparison.Ordinal));
        Assert.All(settings, name => Assert.StartsWith("Settings for ", name, StringComparison.Ordinal));
        Assert.All(chooseAnother, name => Assert.StartsWith("Choose another for ", name, StringComparison.Ordinal));
        Assert.All(chooseAnother.Zip(titles), pair => Assert.EndsWith(pair.Second, pair.First, StringComparison.Ordinal));
        Assert.All(popOuts.Zip(titles), pair => Assert.EndsWith(pair.Second, pair.First, StringComparison.Ordinal));
    }

    [Fact]
    public void APanelWithNoTitleKeepsTheGeneralWordRatherThanNoName()
    {
        foreach (var nothing in new[] { null, "", "   " })
        {
            Assert.Equal("Pop out", BoardText.PopOutName(nothing));
            Assert.Equal("Panel settings", BoardText.PanelSettingsName(nothing));
            Assert.Equal("Choose another", BoardText.ChooseAnotherName(nothing));
        }

        Assert.Equal("Pop out Records", BoardText.PopOutName("  Records  "));
        Assert.Equal("Choose another for Records", BoardText.ChooseAnotherName("  Records  "));
    }

    [Fact]
    public void TheFrameNamesAllThreeToolsFromTheHeadAndTheXamlFixesNone()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", "PanelFrame.xaml"));
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", "PanelFrame.xaml.cs"));

        foreach (var tool in new[] { "PopOutButton", "PanelSettingsButton", "ChooseAnotherButton" })
        {
            var element = Regex.Match(xaml, $"<Button x:Name=\"{tool}\".*?/>", RegexOptions.Singleline);
            Assert.True(element.Success, $"{tool} is no longer a Button in PanelFrame.xaml; this fence is looking in the wrong place.");
            Assert.DoesNotContain("AutomationProperties.Name", element.Value, StringComparison.Ordinal);
        }

        // The tools' names are set where every other tool state is, so a redrawn panel renames them with its title.
        Assert.Contains("AutomationProperties.SetName(PopOutButton, BoardText.PopOutName(title));", code);
        Assert.Contains("AutomationProperties.SetName(PanelSettingsButton, BoardText.PanelSettingsName(title));", code);
        Assert.Contains("AutomationProperties.SetName(ChooseAnotherButton, BoardText.ChooseAnotherName(title));", code);
    }

    /// <summary>
    /// A keyboard move put focus back with Focus() on the board child, a UserControl nothing makes focusable, so
    /// focus fell out of the board after one arrow and the next arrow moved nothing. It goes to the panel's first
    /// tool now, found by the panel's id after the redraw, the way every other tool hand-off already works.
    /// </summary>
    [Fact]
    public void AKeyboardMoveHandsFocusToAToolOfTheMovedPanelNotToThePanel()
    {
        var editing = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.Editing.cs"));
        var frame = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", "PanelFrame.xaml.cs"));

        Assert.DoesNotContain("panel.Focus()", editing, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusPanelLater(index", editing, StringComparison.Ordinal);
        Assert.Contains("FocusPanelLater(panel.Id)", editing, StringComparison.Ordinal);
        Assert.Contains("public bool FocusFirstTool()", frame, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
