using Labs626.UrScore.Core;
using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>One entry in Setup's list. Its text is its title, so a screen reader and UI Automation read the title.</summary>
/// <param name="GameId">The game a game page is for; null for every other page.</param>
public sealed record SetupPage(string Id, string Title, string? GameId = null)
{
    public override string ToString() => Title;
}

/// <summary>A Setup page re-renders from the services when anything changes.</summary>
public interface ISetupPage
{
    void Refresh();
}

/// <summary>
/// Which pages Setup lists, and where it opens (spec "Setup UI > Page list"). A page per game first, then the fixed pages.
/// There is no Recipes page and no Clans page any more: a mode's clans live in its row on the game page.
/// </summary>
public static class SetupPages
{
    public const string Accounts = "accounts";
    public const string Stats = "stats";
    public const string Alerts = "alerts";
    public const string ScoreBook = "score-book";
    public const string Diagnostics = "diagnostics";

    private const string GamePrefix = "game:";

    /// <summary>
    /// Where the board sends you to turn a mode on or pick a clan: the game's page. With no game at all (every manifest failed to
    /// parse) there is no such page, and Diagnostics is where that failure is said.
    /// </summary>
    public static string GamePage(string? gameId) => gameId is null ? Diagnostics : GamePrefix + gameId;

    /// <summary>The game a page id names, or null for a page that isn't a game's.</summary>
    public static string? GameIdOf(string pageId) =>
        pageId.StartsWith(GamePrefix, StringComparison.Ordinal) ? pageId[GamePrefix.Length..] : null;

    /// <summary>One page per game in catalog order, titled by the game's name, then the fixed pages.</summary>
    public static IReadOnlyList<SetupPage> For(GameCatalog catalog)
    {
        var pages = new List<SetupPage>();
        pages.AddRange(catalog.Games.Select(g => new SetupPage(GamePage(g.Id), g.Name, g.Id)));
        pages.Add(new SetupPage(Accounts, "Your accounts"));
        pages.Add(new SetupPage(Stats, "Stats"));
        pages.Add(new SetupPage(Alerts, "Alerts"));
        pages.Add(new SetupPage(ScoreBook, "Score book"));
        pages.Add(new SetupPage(Diagnostics, "Diagnostics"));
        return pages;
    }

    /// <summary>
    /// The first on mode that asks for an input and has no source for its asking reader, as its game and that reader; else null.
    /// The reader is what the game page focuses the clan search of (A11). Only an on mode counts, so a player who upgraded
    /// without Battle is never sent to pick a clan (A5).
    /// </summary>
    public static (string GameId, string AskingSlug)? FirstRun(
        GameCatalog catalog, ModeSwitches switches, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources)
    {
        foreach (var mode in catalog.Modes.Where(m => m.Asks is not null && switches.IsOn(m.Key)))
        {
            if (GameModel.AskingSlug(mode, installed) is not { } slug) continue;
            if (!sources.Any(s => string.Equals(s.Recipe, slug, StringComparison.Ordinal))) return (mode.GameId, slug);
        }

        return null;
    }

    /// <summary>First run (PRD "First run"): the game page while a mode that asks has nothing picked; else null.</summary>
    public static string? FirstRunPage(
        GameCatalog catalog, ModeSwitches switches, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        FirstRun(catalog, switches, installed, sources) is { } first ? GamePage(first.GameId) : null;

    public static string StartPage(
        GameCatalog catalog, ModeSwitches switches, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        FirstRunPage(catalog, switches, installed, sources) ?? For(catalog)[0].Id;
}
