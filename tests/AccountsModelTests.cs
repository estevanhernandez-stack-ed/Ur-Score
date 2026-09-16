using System.ComponentModel;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AccountsModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static InstalledRecipe Sending(Recipe recipe, IReadOnlyList<string>? excluded = null) => new(recipe, "", new RecipeState(
        ExcludedAccountIds: excluded,
        Stats: new Dictionary<string, StatChoice> { [recipe.LastStep.Values[0].Id] = new(Send: true, MetricId: "test.metric") }));

    private static RecipeSnapshot Read(params long[] userIds) =>
        new(WatchState.NoMatches, null, [], [], userIds.Length, "battle=A", [.. userIds.Select(id => RecipeEngineTests.Row(id, 10))], []);

    [Fact]
    public void TheLineSaysWhenRoRoRoLastListedTheAccounts() =>
        Assert.Equal("Accounts come from RoRoRo. Last listed 2 min ago.",
            AccountsModel.ListedLine(new AccountList([Main], true, false, false, Now.AddMinutes(-2)), null, Now));

    [Fact]
    public void WithRoRoRoDownTheSavedListIsNamedAsSuch() =>
        Assert.Equal("Accounts come from RoRoRo. RoRoRo isn't answering, so these are the ones it listed 3 h ago.",
            AccountsModel.ListedLine(new AccountList([Main], false, true, false, null), Now.AddHours(-3), Now));

    [Fact]
    public void BeforeAnyListTheLineSaysSo() =>
        Assert.Equal("Accounts come from RoRoRo. RoRoRo hasn't listed them yet.", AccountsModel.ListedLine(null, null, Now));

    [Fact]
    public void ARefusedListNamesTheCapability() =>
        Assert.StartsWith("Accounts come from RoRoRo. RoRoRo refused to list them: host.queries.accounts is not granted.",
            AccountsModel.ListedLine(new AccountList([], true, false, true, null), null, Now));

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(5 * 3600, "5 h ago")]
    [InlineData(3 * 86400, "3 days ago")]
    public void AgoRoundsDown(int seconds, string text) => Assert.Equal(text, AccountsModel.Ago(Now.AddSeconds(-seconds), Now));

    [Fact]
    public void TurningSendOffExcludesTheAccountAndKeepsOtherEntries()
    {
        var recipe = Sending(Profile, ["not-a-guid", Alt.AccountId.ToString()]);

        var change = AccountsModel.ToggleSend(recipe, [recipe], [Main.AccountId, Alt.AccountId], Main.AccountId, on: false);

        Assert.Null(change.Refusal);
        Assert.Equal(new[] { "not-a-guid", Alt.AccountId.ToString(), Main.AccountId.ToString() }, change.State.ExcludedAccountIds!.ToArray());
    }

    [Fact]
    public void TurningSendOnRemovesTheExclusion()
    {
        var recipe = Sending(Profile, [Alt.AccountId.ToString()]);

        var change = AccountsModel.ToggleSend(recipe, [recipe], [Main.AccountId, Alt.AccountId], Alt.AccountId, on: true);

        Assert.Null(change.Refusal);
        Assert.Empty(change.State.ExcludedAccountIds!);
    }

    [Fact]
    public void TurningSendOnPastRoRoRosLimitIsRefusedAndChangesNothing()
    {
        var accounts = Enumerable.Range(0, HistoryBudget.Limit + 1).Select(_ => Guid.NewGuid()).ToList();
        var recipe = Sending(Profile, [accounts[0].ToString()]);

        var change = AccountsModel.ToggleSend(recipe, [recipe], accounts, accounts[0], on: true);

        Assert.StartsWith("Not allowed: that would use 257 of RoRoRo's 256 history slots.", change.Refusal);
        Assert.Same(recipe.State, change.State);
    }

    [Fact]
    public void FoundInNamesTheClansAnAccountWasReadInMainFirst()
    {
        var clan = Sending(Clan);
        Source[] sources =
        [
            new("s-00000002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine),
            new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main),
            new("s-00000003", Clan.Slug, new Dictionary<string, string> { ["clan"] = "NovaForge" }, SourceRole.Watch),
        ];
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            ["s-00000001"] = Read(101),
            ["s-00000002"] = Read(101),
            ["s-00000003"] = Read(101, 201),
        };

        Assert.Equal("★ CCGP, K0i2", AccountsModel.FoundIn(Main, [clan], sources, latest));
        Assert.Equal("Not in a watched clan", AccountsModel.FoundIn(Alt, [clan], sources, latest));
        Assert.Equal("", AccountsModel.FoundIn(Main, [Sending(Profile)], sources, latest));
    }

    [Fact]
    public void EachRowHasOneSendTickPerSendingRecipe()
    {
        var profile = Sending(Profile, [Alt.AccountId.ToString()]);

        var rows = AccountsModel.Rows([Main, Alt], [profile], [], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("Send estehernandez for Pet Sim 99 profile", rows[0].Sends.Single().Name);
        Assert.True(rows[0].Sends.Single().On);
        Assert.False(rows[1].Sends.Single().On);

        var raised = new List<string?>();
        ((INotifyPropertyChanged)rows[1].Sends[0]).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        rows[1].Sends[0].On = true;
        Assert.Equal(new string?[] { nameof(SendTick.On) }, raised);
    }

    [Fact]
    public void EachSetupRowCarriesItsOwnAccountsPicture()
    {
        var rows = AccountsModel.Rows([Main, Alt], [Sending(Profile)], [], new Dictionary<string, RecipeSnapshot>(),
            id => id == Main.RobloxUserId ? @"C:\cache\avatar-101.png" : null);

        Assert.Equal(@"C:\cache\avatar-101.png", rows[0].Avatar);
        Assert.Null(rows[1].Avatar);

        // With no lookup (a page that hasn't one yet), every row is simply pictureless.
        Assert.Null(AccountsModel.Rows([Main], [Sending(Profile)], [], new Dictionary<string, RecipeSnapshot>())[0].Avatar);
    }

    [Fact]
    public void SetupSaysWhyAnAccountsNumbersAreEmpty()
    {
        var profile = new Source("s-00000009", Profile.Slug, new Dictionary<string, string>(), SourceRole.Mine);
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            [profile.Id] = Read(Main.RobloxUserId) with { Unavailable = new Dictionary<long, string> { [Alt.RobloxUserId] = "Profile is private. Link this account on db.biggames.io and turn on its Profile view." } },
        };
        var installed = new[] { Sending(Profile) };

        var rows = AccountsModel.Rows([Main, Alt], installed, [profile], latest);

        Assert.Equal("", rows.Single(r => r.AccountId == Main.AccountId).Note);
        Assert.Equal(
            "Pet Sim 99 profile: Profile is private. Link this account on db.biggames.io and turn on its Profile view.",
            rows.Single(r => r.AccountId == Alt.AccountId).Note);
        Assert.True(rows.Single(r => r.AccountId == Alt.AccountId).HasNote);
    }
}
