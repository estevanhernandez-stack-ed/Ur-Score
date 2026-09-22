using Labs626.UrScore.Board;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// Every panel type builds its own view, and a type nobody wrote a view for fails here rather than on a board.
/// </summary>
[Collection(WpfCollection.Name)]
public class PanelViewsTests
{
    /// <summary>
    /// The switch used to end in <c>_ =&gt; new LiveLeaderboardPanel()</c>, so a type added to
    /// <see cref="PanelType"/> and forgotten in <c>PanelViews.Create</c> drew a leaderboard: a wrong panel that
    /// looks like a working one, with nothing anywhere saying otherwise (S1-13.13). The default now throws, and
    /// this walks every value of the enum so the throw lands in the suite instead of on somebody's board.
    /// <para>
    /// It asks WHICH exception rather than constructing the panels, and that is not a dodge — it is the only
    /// reading available. Building a real panel runs its XAML, which wants the brushes and styles that live in
    /// <c>App.xaml</c>, and a unit test has no <c>Application</c> to have loaded them; every mapped type therefore
    /// throws <c>XamlParseException</c> here. That is exactly what makes the test work: a type that IS mapped gets
    /// far enough to fail on a missing resource, and only an unmapped one reaches the default and throws
    /// <see cref="ArgumentOutOfRangeException"/>. The two are never confusable.
    /// </para>
    /// <para>
    /// What this does NOT check, said plainly rather than left for someone to assume: that two types do not map to
    /// the SAME view. That needs the constructed controls, so it needs the resources, so it needs a running
    /// application — a smoke walk's job, not this one's. The regression S1-13.13 actually names, a new type
    /// silently becoming a leaderboard, is covered.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPanelTypeHasAViewOfItsOwn()
    {
        var missing = new List<PanelType>();

        UiThread.Run(() =>
        {
            foreach (var type in Enum.GetValues<PanelType>())
            {
                if (Record.Exception(() => PanelViews.Create(type)) is ArgumentOutOfRangeException) missing.Add(type);
            }
        });

        Assert.True(missing.Count == 0, "no view is built for: " + string.Join(", ", missing));
    }

    /// <summary>
    /// A value outside the enum throws rather than quietly becoming a leaderboard. It cannot arrive from disk —
    /// <c>BoardsFile</c> rejects an unknown name with <c>Enum.IsDefined</c> — so this pins the contract for the
    /// only route left, which is a new enum member nobody wired up.
    /// </summary>
    [Fact]
    public void ATypeWithNoViewThrowsRatherThanDrawingTheWrongPanel()
    {
        Exception? thrown = null;

        UiThread.Run(() => thrown = Record.Exception(() => PanelViews.Create((PanelType)9999)));

        var bad = Assert.IsType<ArgumentOutOfRangeException>(thrown);
        Assert.Equal("type", bad.ParamName);
    }
}
