using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
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
    string? Reads, string? Sends, string? Note, string? AskingSlug, string? DimmedLine,
    ModeLinkStatus? LinkStatus = null);

/// <summary>
/// Which of your accounts a mode with a <see cref="ModeLink"/> can read, from its latest live read: one sentence per line,
/// and whether the link button is worth showing (anything not linked, private, or not known yet).
/// </summary>
public sealed record ModeLinkStatus(IReadOnlyList<string> Lines, bool ShowButton, ModeLink Link);

/// <summary><see cref="AccountsLine"/> is where the accounts every mode reads come from: RoRoRo's list.</summary>
public sealed record GameRow(string Id, string Name, bool IsOn, IReadOnlyList<GameModeRow> Modes, string AccountsLine = "");

/// <summary>
/// The game page as data (spec "Setup UI > GamePage"): built from the catalog, the switches and the readers, so every line on
/// the page is the manifest's or <see cref="ModeLines"/>' own and nothing about what a mode reads is typed here.
/// </summary>
public static class GameModel
{
    /// <summary>What the page says when a switch can't be saved because settings.json was unreadable at start.</summary>
    public const string SettingsUnreadable = "Your settings file couldn't be read, so switches can't be saved this session.";

    public const string NoAccounts =
        "Add your Roblox accounts in RoRoRo first. Ur Score reads the list from there; it never signs in to anything itself.";

    public const string NotReadYet = "Not read yet.";

    public const string CouldNotTell = "The last read couldn't tell which accounts are linked.";

    /// <param name="accounts">RoRoRo's list (<see cref="ISetupServices.KnownAccounts"/>); null reads as none.</param>
    /// <param name="sources">The saved sources, to find which snapshots belong to a mode's readers.</param>
    /// <param name="latest">The newest snapshot per source id (<see cref="ISetupServices.Latest"/>).</param>
    public static GameRow For(
        GameDef game, ModeSwitches switches, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<HostAccount>? accounts = null,
        IReadOnlyList<Source>? sources = null, IReadOnlyDictionary<string, RecipeSnapshot>? latest = null)
    {
        accounts ??= [];
        var gameOn = switches.IsGameOn(game.Id);
        var modes = game.Modes.Select(mode =>
        {
            var on = switches.IsOn(mode.Key);
            var asking = mode.Asks is null ? null : AskingSlug(mode, installed);
            var dimmed = asking is null || on ? null : Dimmed(gameOn ? mode.Name : game.Name, installed, asking);

            return new GameModeRow(
                mode.Id, mode.Key, mode.Name, switches.IsModeSet(mode.Key), on, mode.Blurb,
                ModeLines.Reads(mode, installed), ModeLines.Sends(mode, installed), mode.Note, asking, dimmed,
                on && mode.Link is { } link ? LinkStatus(mode, link, accounts, sources ?? [], latest) : null);
        }).ToList();

        return new GameRow(game.Id, game.Name, gameOn, modes, AccountsLine(accounts));
    }

    /// <summary>
    /// Every mode reads the accounts saved in RoRoRo, never a sign-in of Ur Score's own, so the page says how many there are,
    /// or where to add them when there are none.
    /// </summary>
    public static string AccountsLine(IReadOnlyList<HostAccount> accounts) => accounts.Count switch
    {
        0 => NoAccounts,
        1 => "Ur Score uses the 1 account saved in RoRoRo.",
        var n => $"Ur Score uses the {n} accounts saved in RoRoRo.",
    };

    /// <summary>
    /// Linked or not, from the mode's latest LIVE reads (a remembered snapshot is the score book's, which knows nothing of
    /// linking): an account that came back with data is linked; the source's 404 (<see cref="UnavailableReason.NotFound"/>) is
    /// not linked; the reader's own <c>unavailable</c> (<see cref="UnavailableReason.Declared"/>) is linked but private. By the
    /// typed reason, never by the message, so rewording a reader can't move an account between groups. A 400 is neither and
    /// counts in neither. "Profile view" is the mode's name: only Profile carries a link today.
    /// </summary>
    private static ModeLinkStatus LinkStatus(
        ModeDef mode, ModeLink link, IReadOnlyList<HostAccount> accounts, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot>? latest)
    {
        var snapshots = sources
            .Where(s => mode.Reads.Contains(s.Recipe, StringComparer.Ordinal))
            .Select(s => latest?.GetValueOrDefault(s.Id))
            .OfType<RecipeSnapshot>()
            .Where(s => s.RememberedAt is null)
            .ToList();

        if (snapshots.Count == 0) return new ModeLinkStatus([NotReadYet], ShowButton: true, link);

        var read = snapshots.SelectMany(s => s.Rows ?? []).Select(r => r.UserId).ToHashSet();
        var reasons = new Dictionary<long, UnavailableReason>();
        foreach (var (id, why) in snapshots.SelectMany(s => s.UnavailableReasons)) reasons.TryAdd(id, why);

        // Your accounts in RoRoRo's order, then any id a read named that the list no longer holds.
        var order = accounts.Where(a => a.RobloxUserId > 0).Select(a => a.RobloxUserId).ToList();
        var ids = order.Concat(read.Concat(reasons.Keys).Where(id => !order.Contains(id)).Order()).Distinct()
            .Where(id => read.Contains(id) || reasons.ContainsKey(id))
            .ToList();
        string Name(long id) => accounts.FirstOrDefault(a => a.RobloxUserId == id)?.DisplayName ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var notLinked = ids.Where(id => !read.Contains(id) && reasons.GetValueOrDefault(id, UnavailableReason.BadRequest) == UnavailableReason.NotFound).ToList();
        var hidden = ids.Where(id => !read.Contains(id) && reasons.GetValueOrDefault(id, UnavailableReason.BadRequest) == UnavailableReason.Declared).ToList();
        var linked = ids.Count(read.Contains) + hidden.Count;
        var total = linked + notLinked.Count;

        if (total == 0) return new ModeLinkStatus([CouldNotTell], ShowButton: true, link);

        var lines = new List<string>
        {
            linked == total
                ? total == 1 ? "Your account is linked." : $"All {total} accounts are linked."
                : $"{linked} of {total} {(total == 1 ? "account" : "accounts")} {(linked == 1 ? "is" : "are")} linked.",
        };
        if (notLinked.Count > 0) lines.Add($"Not linked: {string.Join(", ", notLinked.Select(Name))}.");
        if (hidden.Count > 0) lines.Add($"Linked, but the {mode.Name} view is private: {string.Join(", ", hidden.Select(Name))}.");

        return new ModeLinkStatus(lines, ShowButton: notLinked.Count + hidden.Count > 0, link);
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
