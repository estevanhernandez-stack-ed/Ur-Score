using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class DiagnosticsModelTests
{
    private const string FakeKey = "SECRET-KEY-VALUE-123";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "BirchMain");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static InstalledRecipe Installed => new(Clan, "", new RecipeState());

    private static Source ForClan => new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

    [Theory]
    [InlineData(WatchState.Reporting, "Reporting to RoRoRo.")]
    [InlineData(WatchState.HostDown, "RoRoRo is not running.")]
    [InlineData(WatchState.SourceIdle, "Nothing to read right now.")]
    [InlineData(WatchState.Showing, "Reading. No stat is set to send to RoRoRo.")]
    public void StatesReadAsSentences(WatchState state, string text) => Assert.Equal(text, DiagnosticsModel.StateText(state));

    /// <summary>
    /// Backlog S1-12.8. After Stop the page still said "Reporting to RoRoRo.", news about now from a read that had finished.
    /// Stopped, a source's state is its last read's, and a state that claimed something was happening says what happened.
    /// </summary>
    [Theory]
    [InlineData(WatchState.Reporting, "Stopped. Last read: Reported to RoRoRo.")]
    [InlineData(WatchState.Showing, "Stopped. Last read: No stat was set to send to RoRoRo, so nothing was sent.")]
    [InlineData(WatchState.HostDown, "Stopped. Last read: RoRoRo wasn't running.")]
    [InlineData(WatchState.SourceIdle, "Stopped. Last read: There was nothing to read.")]
    [InlineData(WatchState.SourceUnreachable, "Stopped. Last read: Could not reach the data.")]
    public void AfterStopAStateIsTheLastReadsNeverNewsAboutNow(WatchState state, string text)
    {
        var source = ForClan;
        var latest = new Dictionary<string, RecipeSnapshot> { [source.Id] = new(state, null, [], [], 1) };
        SourceDiagnostic Row(bool running) =>
            DiagnosticsModel.Sources([Installed], [source], latest, _ => Now.AddMinutes(-3), running, [Main], Now, new Redactor(() => [])).Single();

        Assert.Equal(text, Row(running: false).State);
        Assert.Equal(DiagnosticsModel.StateText(state), Row(running: true).State);
    }

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
        Assert.Equal("once reading starts", stopped.NextRead);
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
        Assert.Contains("BirchMain · Points: no 'Points' here", misses);
        Assert.DoesNotContain("999", misses);
        Assert.DoesNotContain("someone else's", misses);
    }

    /// <summary>
    /// Id 0 is RoRoRo's "no Roblox id known for this account", so it is not an account id at all — it is the
    /// ABSENCE of one, and several accounts can carry it at once. Matching on it would take the first account
    /// that happened to have no id and put ITS name against a miss belonging to another, which is a diagnostic
    /// that names the wrong person. The filter that prevents it is one clause of one condition and nothing
    /// pinned it (S1-12.11).
    /// <para>
    /// Two id-0 accounts in the fixture, deliberately: with one, dropping the filter names that one account and
    /// the test would still look reasonable. With two, dropping it names whichever comes first and the
    /// misattribution is what fails.
    /// </para>
    /// </summary>
    [Fact]
    public void AMissAgainstAnAccountWithNoRobloxIdNamesNobody()
    {
        var noId = new HostAccount(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0, "PendingOne");
        var alsoNoId = new HostAccount(Guid.Parse("44444444-4444-4444-4444-444444444444"), 0, "PendingTwo");
        var snapshot = new RecipeSnapshot(WatchState.Reporting, null, [], [], 2)
        {
            CellMisses = new Dictionary<(long UserId, string Stat), string> { [(0, "value")] = "no 'Points' here" },
        };

        var misses = DiagnosticsModel.Misses(Clan, snapshot, [noId, alsoNoId, Main]);

        Assert.Equal("", misses);
        Assert.DoesNotContain("PendingOne", misses, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingTwo", misses, StringComparison.Ordinal);

        // And the account WITH an id is still named, so this is the id-0 clause doing the work and not the whole
        // cell-miss line having gone quiet.
        var named = new RecipeSnapshot(WatchState.Reporting, null, [], [], 2)
        {
            CellMisses = new Dictionary<(long UserId, string Stat), string> { [(101, "value")] = "no 'Points' here" },
        };
        Assert.Contains("BirchMain", DiagnosticsModel.Misses(Clan, named, [noId, alsoNoId, Main]), StringComparison.Ordinal);
    }

    /// <summary>
    /// A watch source's inputs go in the copy text like every other source's. It reads a clan it does not record
    /// for, and that clan's name is exactly what someone reading a diagnostic needs in order to see WHICH clan a
    /// watch is pointed at — a row saying role=Watch with no inputs describes nothing. Kept honest against the
    /// privacy line rather than assumed: clan names may be written down (owner's ruling, 2026-09-20); player ids
    /// and names still may not, which the redaction tests above cover and this does not weaken (S1-12.11).
    /// </summary>
    [Fact]
    public void AWatchSourcesInputsAreInTheCopyTextLikeAnyOthers()
    {
        var watch = new Source("s-00000002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "H8ER" }, SourceRole.Watch);
        var redactor = new Redactor(() => []);
        var rows = DiagnosticsModel.Sources(
            [Installed], [ForClan, watch], new Dictionary<string, RecipeSnapshot>(), _ => Now.AddMinutes(-10), true, [Main], Now, redactor);

        var copy = DiagnosticsModel.CopyText(Now, [Installed], [ForClan, watch], rows, false, "host", "book", 0, 0, [], redactor);

        var watchLine = copy.Split(Environment.NewLine).Single(l => l.StartsWith("source=s-00000002", StringComparison.Ordinal));
        Assert.EndsWith("role=Watch enabled=True inputs=clan=H8ER", watchLine, StringComparison.Ordinal);
        Assert.Contains("inputs=clan=CCGP", copy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConflictsNameOwnAccountsAndOwnerAsLastReadEvidence(bool running)
    {
        var source = ForClan with { Id = "reader" };
        var owner = ForClan with { Id = "owner", Inputs = new Dictionary<string, string> { ["clan"] = "Other" } };
        var snapshot = new RecipeSnapshot(WatchState.NoMatches, null, [], [], 2)
        {
            RecipeSlug = Clan.Slug,
            ClaimConflicts = new Dictionary<long, string> { [101] = owner.Id, [999] = "foreign-secret" },
        };
        var redactor = new Redactor(() => []);
        var rows = DiagnosticsModel.Sources([Installed], [source, owner], new Dictionary<string, RecipeSnapshot> { [source.Id] = snapshot },
            _ => Now.AddMinutes(-10), running, [Main], Now, redactor);
        var row = rows[0];

        Assert.True(row.HasClaimConflicts);
        Assert.Contains("were claimed by another source", row.State);
        Assert.Equal("Last read: BirchMain was skipped for recording and sending here because Other · Pet Sim 99 clan battle points held the account's claim. This does not confirm a successful send or current membership.", row.ClaimConflicts);
        Assert.False(rows[1].HasClaimConflicts);
        var copy = DiagnosticsModel.CopyText(Now, [Installed], [source, owner], rows, false, "host", "book", 0, 0, [], redactor);
        Assert.Contains(row.ClaimConflicts, copy);
        Assert.DoesNotContain("999", copy);
        Assert.DoesNotContain("foreign-secret", copy);
        Assert.Equal("", DiagnosticsModel.ClaimConflictText(Clan, snapshot, [source, owner], []));
    }

    [Fact]
    public void ConflictOwnerRemovalAndRecipeChangesDoNotExposeStaleIdentities()
    {
        var source = ForClan;
        var snapshot = new RecipeSnapshot(WatchState.Showing, null, [], [], 1)
        {
            RecipeSlug = Clan.Slug,
            ClaimConflicts = new Dictionary<long, string> { [101] = "removed-owner" },
        };
        Assert.Contains("a source no longer configured held", DiagnosticsModel.ClaimConflictText(Clan, snapshot, [source], [Main]));
        var row = DiagnosticsModel.Sources([Installed], [source], new Dictionary<string, RecipeSnapshot>
        {
            [source.Id] = snapshot with { RecipeSlug = "different-recipe" },
        }, _ => Now, true, [Main], Now, new Redactor(() => [])).Single();
        Assert.False(row.HasClaimConflicts);
    }

    [Fact]
    public void ConflictTextIsRedactedForDisplayAndCopy()
    {
        var owner = ForClan with { Id = "owner", Inputs = new Dictionary<string, string> { ["clan"] = FakeKey } };
        var snapshot = new RecipeSnapshot(WatchState.Showing, null, [], [], 1)
        {
            RecipeSlug = Clan.Slug,
            ClaimConflicts = new Dictionary<long, string> { [101] = owner.Id },
        };
        var redactor = new Redactor(() => [FakeKey]);
        var rows = DiagnosticsModel.Sources([Installed], [ForClan, owner], new Dictionary<string, RecipeSnapshot> { [ForClan.Id] = snapshot },
            _ => Now, true, [Main], Now, redactor);
        Assert.Contains(Redactor.Mask, rows[0].ClaimConflicts);
        Assert.DoesNotContain(FakeKey, rows[0].ClaimConflicts);
        var copy = DiagnosticsModel.CopyText(Now, [Installed], [ForClan, owner], rows, false, "host", "book", 0, 0, [], redactor);
        Assert.DoesNotContain(FakeKey, copy);
    }

    [Fact]
    public void CopiedDiagnosticsHideKeysKeepTheLastFortyTrailLinesAndNoBookContent()
    {
        var source = ForClan;
        var row = new SourceDiagnostic(source.Id, "CCGP", "Reporting to RoRoRo.", $"detail {FakeKey}", "1m ago", "in 2m", "");
        var trail = Enumerable.Range(0, 50).Select(i => $"trail-{i:00};").ToList();

        var text = DiagnosticsModel.CopyText(Now, [Installed], [source], [row], resolveNames: true, "host=1.28.0.0 reject=(none)",
            @"C:\data\scorebook", bookPending: 2, bookDropped: 0, trail, new Redactor(() => [FakeKey]));

        Assert.DoesNotContain(FakeKey, text);
        Assert.DoesNotContain("raw response", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last-response", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Redactor.Mask, text);
        Assert.Contains("score book in C:\\data\\scorebook: pending=2 dropped=0 (no book content is included)", text);
        Assert.Contains($"source={source.Id} recipe={Clan.Slug} role=Main enabled=True inputs=clan=CCGP", text);
        Assert.DoesNotContain("trail-09;", text);
        Assert.Contains("trail-10;", text);
        Assert.Contains("trail-49;", text);
    }
}
