using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

[Collection(WpfCollection.Name)]
public class StatsTableTests
{
    /// <summary>
    /// A Send tick the budget refuses is undone and the table redrawn ONCE. It was drawn three times: once for
    /// the tick itself, once when undoing the tick raised the same change event, and once more by the undo's
    /// own explicit redraw (S1-10.2). Three redraws of the same lines is not a fault anybody could see, but each
    /// raises <c>Changed</c>, and the host enables its Save button from that — so the count is the thing to pin.
    /// <para>
    /// The budget refuses because 300 accounts times one sent stat is past RoRoRo's 256-slot ceiling. The undo is
    /// posted to the dispatcher, so the test pumps it before looking.
    /// </para>
    /// </summary>
    [Fact]
    public void ARefusedSendTickIsUndoneWithOneRedraw() => UiThread.RunInApp(() =>
    {
        var recipe = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var ids = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();
        var table = new StatsTable();
        table.Load(recipe, new RecipeState(), [], () => ids, _ => "", null, "Read names");
        var row = ((IEnumerable<StatRow>)((ListBox)table.FindName("StatsRows")).ItemsSource).First(r => r.Offered);
        var redraws = 0;
        table.Changed += (_, _) => redraws++;

        row.Send = true;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

        Assert.False(row.Send);
        Assert.Equal(1, redraws);
    });
}
