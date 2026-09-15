using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class DiagnosticsModelTests
{
    private const string FakeKey = "SECRET-KEY-VALUE-123";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static InstalledRecipe Installed => new(Clan, "", new RecipeState());

    private static Source ForClan => new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

    [Theory]
    [InlineData(WatchState.Reporting, "Reporting to RoRoRo.")]
    [InlineData(WatchState.HostDown, "RoRoRo is not running.")]
    [InlineData(WatchState.SourceIdle, "Nothing to read right now.")]
    [InlineData(WatchState.Showing, "Reading. No stat is set to send to RoRoRo.")]
    public void StatesReadAsSentences(WatchState state, string text) => Assert.Equal(text, DiagnosticsModel.StateText(state));

    [Fact]
    public void EachSourceSaysWhenItWasReadAndWhenItReadsNext()
    {
        var source = ForClan;
        var snapshot = new RecipeSnapshot(WatchState.Reporting, $"Reporting 1 of 50 row(s). key={FakeKey}", [], [], 50);
        var latest = new Dictionary<string, RecipeSnapshot> { [source.Id] = snapshot };
        var redactor = new Redactor(() => [FakeKey]);

        var justRead = DiagnosticsModel.Sources([Installed], [source], latest, _ => Now.AddMinutes(-1), running: true, [Main], Now, redactor).Single();
        var late = DiagnosticsModel.Sources([Installed], [source], latest, _ => Now.AddMinutes(-5), running: true, [Main], Now, redactor).Single();
        var stopped = DiagnosticsModel.Sources([Installed], [source], latest, _ => null, running: false, [Main], Now, redactor).Single();

        Assert.Equal("CCGP · Pet Sim 99 clan battle points", justRead.Name);
        Assert.Equal("Reporting to RoRoRo.", justRead.State);
        Assert.Equal($"{StatText.Span(TimeSpan.FromMinutes(1))} ago", justRead.LastRead);
        Assert.Equal($"in {StatText.Span(TimeSpan.FromSeconds(Clan.EffectiveEverySeconds - 60))}", justRead.NextRead);
        Assert.DoesNotContain(FakeKey, justRead.Detail);
        Assert.Equal("due now", late.NextRead);
        Assert.Equal("never", stopped.LastRead);
        Assert.Equal("when you press Start", stopped.NextRead);
    }

    [Fact]
    public void MissesNameOnlyYourOwnAccounts()
    {
        var snapshot = new RecipeSnapshot(WatchState.Reporting, null, [], [], 2)
        {
            StatMisses = new Dictionary<string, string> { ["value"] = "no 'Points' in any row" },
            CellMisses = new Dictionary<(long UserId, string Stat), string>
            {
                [(101, "value")] = "no 'Points' here",
                [(999, "value")] = "someone else's miss",
            },
        };

        var misses = DiagnosticsModel.Misses(Clan, snapshot, [Main]);

        Assert.Contains("Points: no 'Points' in any row", misses);
        Assert.Contains("estehernandez · Points: no 'Points' here", misses);
        Assert.DoesNotContain("999", misses);
        Assert.DoesNotContain("someone else's", misses);
    }

    [Fact]
    public void CopiedDiagnosticsHideKeysKeepTheLastFortyTrailLinesAndNoBookContent()
    {
        var source = ForClan;
        var row = new SourceDiagnostic(source.Id, "CCGP", "Reporting to RoRoRo.", $"detail {FakeKey}", "1m ago", "in 2m", "");
        var trail = Enumerable.Range(0, 50).Select(i => $"trail-{i:00};").ToList();

        var text = DiagnosticsModel.CopyText(Now, [Installed], [source], [row], resolveNames: true, "host=1.28.0.0 reject=(none)",
            @"C:\data\last-response", @"C:\data\scorebook", bookPending: 2, bookDropped: 0, trail, new Redactor(() => [FakeKey]));

        Assert.DoesNotContain(FakeKey, text);
        Assert.Contains(Redactor.Mask, text);
        Assert.Contains("score book in C:\\data\\scorebook: pending=2 dropped=0 (no book content is included)", text);
        Assert.Contains($"source={source.Id} recipe={Clan.Slug} role=Main enabled=True inputs=clan=CCGP", text);
        Assert.DoesNotContain("trail-09;", text);
        Assert.Contains("trail-10;", text);
        Assert.Contains("trail-49;", text);
    }
}
