using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ManifestTests
{
    private const string Clan = "pet-sim-99-clan-battle-points";
    private const string Top = "pet-sim-99-top-clans";
    private const string Profile = "pet-sim-99-profile";

    private static ModeDef Mode(
        string id = "battle", string[]? reads = null, string? asks = "clan", string? board = "battle", string[]? shows = null) =>
        new("g", id, id, "", reads ?? [Clan, Top], asks, null, board, true, shows ?? []);

    private static IReadOnlyList<string> Problems(params ModeDef[] modes) =>
        GameCatalog.Validate(new GameCatalog([new GameDef("g", "G", modes)]), BuiltInRecipes.BySlug);

    [Fact]
    public void TheShippedManifestHasNoProblemsAgainstTheShippedRecipes()
    {
        Assert.NotEmpty(GameCatalog.BuiltIn.Games);
        Assert.Empty(GameCatalog.Validate(GameCatalog.BuiltIn, BuiltInRecipes.BySlug));
    }

    [Fact]
    public void ThePetSimManifestIsWhatTheSpecSays()
    {
        var game = Assert.Single(GameCatalog.BuiltIn.Games);
        Assert.Equal("pet-sim-99", game.Id);
        Assert.Equal(["pet-sim-99/battle", "pet-sim-99/profile"], game.Modes.Select(m => m.Key));

        var battle = GameCatalog.BuiltIn.Find("pet-sim-99/battle")!;
        Assert.Equal([Clan, Top], battle.Reads);
        Assert.Equal("clan", battle.Asks);
        Assert.Equal([$"{Clan}:value"], battle.Shows);
        Assert.Same(battle, GameCatalog.BuiltIn.ModeOf(Top));
        Assert.Equal("pet-sim-99/profile", GameCatalog.BuiltIn.ModeOf(Profile)!.Key);
        Assert.Null(GameCatalog.BuiltIn.ModeOf("somebody-elses-recipe"));
    }

    [Fact]
    public void AGoodCraftedModeHasNoProblems() => Assert.Empty(Problems(Mode(shows: [$"{Clan}:value"])));

    [Fact]
    public void AnUnknownSlugIsRejected() =>
        Assert.Contains(Problems(Mode(reads: ["nope"], asks: null)), p => p.Contains("'nope'") && p.Contains("not a recipe that ships"));

    [Fact]
    public void ASlugInTwoModesIsRejected() =>
        Assert.Contains(Problems(Mode("a", [Profile], null, null), Mode("b", [Profile], null, null)), p => p.Contains("read by both"));

    [Fact]
    public void ABoardThatIsNotAStarterIsRejected() =>
        Assert.Contains(Problems(Mode(board: "elsewhere")), p => p.Contains("not a starter board"));

    [Fact]
    public void AsksThatIsNotAnInputOfTheModesRecipesIsRejected()
    {
        Assert.Contains(Problems(Mode(asks: "nobody")), p => p.Contains("not an input"));
        Assert.Contains(Problems(Mode(reads: [Profile], asks: "clan", board: "alts")), p => p.Contains("not an input"));
    }

    [Fact]
    public void AModeWithNoReadsIsRejected() =>
        Assert.Contains(Problems(Mode(reads: [], asks: null)), p => p.Contains("reads no recipes"));

    [Fact]
    public void AShowsEntryThatIsNotAStatOfTheModesReadsIsRejected()
    {
        Assert.Contains(Problems(Mode(shows: ["value"])), p => p.Contains("slug:key"));
        Assert.Contains(Problems(Mode(shows: [$"{Profile}:diamonds"])), p => p.Contains("slug:key"));
        Assert.Contains(Problems(Mode(shows: [$"{Clan}:nonsense"])), p => p.Contains("offers no stat 'nonsense'"));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        Assert.Contains(Problems(Mode("a", [Clan], "clan"), Mode("a", [Top], null)), p => p.Contains("two modes with the id 'a'"));

        var twice = new GameCatalog([new GameDef("g", "G", [Mode()]), new GameDef("g", "G", [Mode("b", [Profile], null, "alts")])]);
        Assert.Contains(GameCatalog.Validate(twice, BuiltInRecipes.BySlug), p => p.Contains("Two games have the id 'g'"));
    }

    [Fact]
    public void ParseRejectsWhatItCannotRead()
    {
        Assert.Contains("version", Assert.Throws<GameManifestException>(() => GameCatalog.Parse("""{"game": 2}""")).Message);
        Assert.Contains("no id", Assert.Throws<GameManifestException>(() => GameCatalog.Parse("""{"game": 1}""")).Message);
        Assert.Contains("no name", Assert.Throws<GameManifestException>(() => GameCatalog.Parse("""{"game": 1, "id": "g"}""")).Message);
        Assert.Contains("no modes", Assert.Throws<GameManifestException>(() => GameCatalog.Parse("""{"game": 1, "id": "g", "name": "G", "modes": []}""")).Message);
        Assert.Contains("two modes", Assert.Throws<GameManifestException>(() => GameCatalog.Parse(
            """{"game": 1, "id": "g", "name": "G", "modes": [{"id": "a", "name": "A"}, {"id": "a", "name": "A"}]}""")).Message);
        Assert.Throws<GameManifestException>(() => GameCatalog.Parse("not json"));
    }
}
