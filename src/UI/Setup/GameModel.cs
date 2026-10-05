using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// One mode's row on the game page. <see cref="IsSet"/> is the mode's own switch (what its toggle shows); <see cref="IsOn"/>
/// is whether it reads, which also needs its game on. <see cref="AskingSlug"/> is the reader its clan section belongs to, null
/// for a mode that asks nothing. <see cref="DimmedLine"/> is said over that section while the mode doesn't read.
/// </summary>
public sealed record GameModeRow(
    string Id, string Key, string Name, bool IsSet, bool IsOn, string Blurb,
    string? Reads, string? Sends, string? Note, string? AskingSlug, string? DimmedLine);

public sealed record GameRow(string Id, string Name, bool IsOn, IReadOnlyList<GameModeRow> Modes);

/// <summary>
/// The game page as data (spec "Setup UI > GamePage"): built from the catalog, the switches and the readers, so every line on
/// the page is the manifest's or <see cref="ModeLines"/>' own and nothing about what a mode reads is typed here.
/// </summary>
public static class GameModel
{
    /// <summary>What the page says when a switch can't be saved because settings.json was unreadable at start.</summary>
    public const string SettingsUnreadable = "Your settings file couldn't be read, so switches can't be saved this session.";

    public static GameRow For(GameDef game, ModeSwitches switches, IReadOnlyList<InstalledRecipe> installed)
    {
        var gameOn = switches.IsGameOn(game.Id);
        var modes = game.Modes.Select(mode =>
        {
            var on = switches.IsOn(mode.Key);
            var asking = mode.Asks is null ? null : AskingSlug(mode, installed);
            var dimmed = asking is null || on ? null : Dimmed(gameOn ? mode.Name : game.Name, installed, asking);

            return new GameModeRow(
                mode.Id, mode.Key, mode.Name, switches.IsModeSet(mode.Key), on, mode.Blurb,
                ModeLines.Reads(mode, installed), ModeLines.Sends(mode, installed), mode.Note, asking, dimmed);
        }).ToList();

        return new GameRow(game.Id, game.Name, gameOn, modes);
    }

    /// <summary>
    /// The reader a mode asks through: the one of its reads whose inputs hold the mode's <c>asks</c>, in manifest order. Not
    /// simply the first read, which for Battle could as well be the clans list, which has no input. Null when nothing asks.
    /// </summary>
    public static string? AskingSlug(ModeDef mode, IReadOnlyList<InstalledRecipe> installed)
    {
        if (mode.Asks is not { } asks) return null;

        return mode.Reads
            .Select(slug => installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal))?.Recipe)
            .FirstOrDefault(r => r is not null && r.Inputs.Any(i => string.Equals(i.Id, asks, StringComparison.Ordinal)))?.Slug;
    }

    /// <summary>
    /// Why a switch didn't change, in plain words for the page's status line: the settings file unreadable this session, or
    /// the write's own reason. Never an exception out of a click.
    /// </summary>
    public static string SwitchProblem(Exception ex) =>
        ex is InvalidOperationException && ex.Message == AppServices.SettingsNotWritten
            ? SettingsUnreadable
            : $"Could not save that: {ex.Message}";

    /// <summary>Names the switch that's actually off: the mode's, or the game's when the game is off.</summary>
    private static string Dimmed(string what, IReadOnlyList<InstalledRecipe> installed, string asking)
    {
        var recipe = installed.First(i => string.Equals(i.Recipe.Slug, asking, StringComparison.Ordinal)).Recipe;
        return $"Turn {what} on to read these {RecipeWords.GroupsLower(recipe)}.";
    }
}
