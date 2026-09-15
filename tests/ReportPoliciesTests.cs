using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ReportPoliciesTests
{
    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "Main account");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "Alt account");

    private static readonly RecipeState ExcludingAlt = new(ExcludedAccountIds: [Alt.AccountId.ToString()]);

    private static InstalledRecipe Installed(string fixture, RecipeState? state = null)
    {
        var text = RecipeParserTests.Fixture(fixture);
        return new InstalledRecipe(RecipeParser.Parse(text).Recipe!, text, state ?? new RecipeState());
    }

    private static InstalledRecipe ClanRecipe => Installed("petsim99-clan-battle.recipe.json", ExcludingAlt);

    private static Source SourceOf(InstalledRecipe installed, string id, SourceRole role, bool enabled = true) =>
        new(id, installed.Recipe.Slug, new Dictionary<string, string> { ["clan"] = id }, role, enabled);

    [Fact]
    public void YourKnownAccountsMinusTheExcludedOnesMaySend()
    {
        var clan = ClanRecipe;

        var allowed = ReportPolicies.Allowed(clan, [Main, Alt], [SourceOf(clan, "s-00000001", SourceRole.Mine)]);

        Assert.Equal(new[] { Main.AccountId }, allowed.ToArray());
    }

    [Fact]
    public void AGroupListSendsNothing()
    {
        var top = Installed("petsim99-top-clans.recipe.json");

        Assert.False(ReportPolicies.SendsByRole(top, [SourceOf(top, "s-00000001", SourceRole.Mine)]));
        Assert.Empty(ReportPolicies.Allowed(top, [Main, Alt], [SourceOf(top, "s-00000001", SourceRole.Mine)]));
    }

    [Fact]
    public void ARecipeWhoseSourcesAreAllWatchedSendsNothing()
    {
        var clan = ClanRecipe;
        Source[] sources = [SourceOf(clan, "s-00000001", SourceRole.Watch), SourceOf(clan, "s-00000002", SourceRole.Watch)];

        Assert.False(ReportPolicies.SendsByRole(clan, sources));
        Assert.Empty(ReportPolicies.Allowed(clan, [Main, Alt], sources));
    }

    [Fact]
    public void AMixOfWatchedAndOwnSourcesSendsForKnownMinusExcluded()
    {
        var clan = ClanRecipe;
        Source[] sources = [SourceOf(clan, "s-00000001", SourceRole.Watch), SourceOf(clan, "s-00000002", SourceRole.Main)];

        Assert.True(ReportPolicies.SendsByRole(clan, sources));
        Assert.Equal(new[] { Main.AccountId }, ReportPolicies.Allowed(clan, [Main, Alt], sources).ToArray());
    }

    [Fact]
    public void AnotherRecipesSourceOrASwitchedOffOneDoesNotMakeThisRecipeSend()
    {
        var clan = ClanRecipe;
        var profile = Installed("petsim99-profile.recipe.json");
        Source[] sources =
        [
            SourceOf(clan, "s-00000001", SourceRole.Watch),
            SourceOf(clan, "s-00000002", SourceRole.Mine, enabled: false),
            SourceOf(profile, "s-00000003", SourceRole.Mine),
        ];

        Assert.Empty(ReportPolicies.Allowed(clan, [Main, Alt], sources));
    }

    [Fact]
    public void ARecipeWithNoSourceYetDescribesWhatItsOwnSourcesWouldSend() =>
        Assert.Equal(new[] { Main.AccountId }, ReportPolicies.Allowed(ClanRecipe, [Main, Alt], []).ToArray());
}
