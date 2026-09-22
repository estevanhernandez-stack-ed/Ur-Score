using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

public enum SetupKind { Recipe, Clan, Board, Key, Stats }

public enum SetupOutcome { Add, Update, Same, Replace, Kept, EnterAgain }

/// <summary>
/// One line of the preview: what kind of thing, its name, what importing would do to it and why, whether it starts
/// ticked, and — for a clan — the recipe it needs. <see cref="FileId"/> and <see cref="LocalId"/> are a clan's ids on
/// each side; an Add has no local id yet, and Apply mints one.
/// </summary>
public sealed record SetupItem(
    SetupKind Kind, string Key, string Name, SetupOutcome Outcome, string Note, bool Ticked,
    string? DependsOnRecipe = null, string? FileId = null, string? LocalId = null);

/// <summary>This machine's setup, as the plan needs it.</summary>
public sealed record SetupHere(
    IReadOnlyList<InstalledRecipe> Installed, IReadOnlyList<Source> Sources, IReadOnlyList<BoardDef> SavedBoards, IReadOnlyList<HostAccount> Accounts);

public sealed record SetupMergePlan(IReadOnlyList<SetupItem> Items, SetupPack File)
{
    /// <summary>A clan can be ticked only when its recipe is ticked or already installed (spec §2, the one dependency).</summary>
    public bool CanTick(SetupItem item, IReadOnlySet<string> tickedKeys) =>
        item.DependsOnRecipe is not { } slug || tickedKeys.Contains("recipe:" + slug) || RecipeInstalled.Contains(slug);

    /// <summary>The items that will be applied: ticked, tickable, and with something to do.</summary>
    public IEnumerable<SetupItem> Ticked(IReadOnlySet<string> tickedKeys) =>
        Items.Where(i => i.Outcome is SetupOutcome.Add or SetupOutcome.Update or SetupOutcome.Replace && tickedKeys.Contains(i.Key) && CanTick(i, tickedKeys));

    /// <summary>Set by <see cref="SetupMerge.Plan"/>: the slugs installed here, for <see cref="CanTick"/>.</summary>
    internal IReadOnlySet<string> RecipeInstalled { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Importing a setup: the plan, pure (spec §2), and the apply (spec §4, Task 6). Identity: a recipe by slug, a clan by
/// recipe and clan name (case and spaces aside, as the book import matches), a board by name. The file wins where
/// both have a thing and it differs; a clan keeps THIS machine's id; sends arrive off.
/// </summary>
public static class SetupMerge
{
    public static SetupMergePlan Plan(SetupPack file, SetupHere here, int readings, int finals)
    {
        var items = new List<SetupItem>();
        var installedHere = here.Installed.ToDictionary(i => i.Recipe.Slug, StringComparer.Ordinal);

        var fileRecipeSlugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in file.Recipes)
        {
            fileRecipeSlugs.Add(recipe.Slug);
            var key = "recipe:" + recipe.Slug;
            if (installedHere.TryGetValue(recipe.Slug, out var local))
            {
                var differs = !string.Equals(local.Text, recipe.Text, StringComparison.Ordinal);
                items.Add(differs
                    ? new SetupItem(SetupKind.Recipe, key, recipe.Name, SetupOutcome.Update, "the file's copy differs; its ticks come with it, sends off", Ticked: true)
                    : new SetupItem(SetupKind.Recipe, key, recipe.Name, SetupOutcome.Same, "same as here", Ticked: false));
            }
            else
            {
                items.Add(new SetupItem(SetupKind.Recipe, key, recipe.Name, SetupOutcome.Add, "sends off until you tick them", Ticked: true));
            }
        }

        // A recipe the file doesn't carry is still an account of this machine's setup (spec §2: anything only here is
        // Kept, listed). First-wins is moot here — installedHere is already keyed by slug, one entry per recipe.
        foreach (var installed in here.Installed.Where(i => !fileRecipeSlugs.Contains(i.Recipe.Slug)))
        {
            items.Add(new SetupItem(SetupKind.Recipe, "recipe:" + installed.Recipe.Slug, installed.Recipe.Name, SetupOutcome.Kept, "only this PC has it", Ticked: false));
        }

        var fileNames = file.Sources.Select(s => s.Recipe + "|" + s.InputsKey).ToHashSet(StringComparer.Ordinal);
        foreach (var source in file.Sources)
        {
            var identity = source.Recipe + "|" + source.InputsKey;
            var name = ClanName(source);
            var local = here.Sources.FirstOrDefault(s => string.Equals(s.Recipe + "|" + s.InputsKey, identity, StringComparison.Ordinal));
            if (local is null)
            {
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Add, RoleWord(source), Ticked: true, DependsOnRecipe: source.Recipe, FileId: source.Id));
            }
            else if (local.Role != source.Role || local.Enabled != source.Enabled)
            {
                var note = local.Role != source.Role
                    ? $"here it is {RoleWord(local)}; the file says {RoleWord(source)}"
                    : source.Enabled ? "here it is switched off; the file has it on" : "here it is on; the file has it switched off";
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Replace, note, Ticked: true, DependsOnRecipe: source.Recipe, FileId: source.Id, LocalId: local.Id));
            }
            else
            {
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Same, "same as here", Ticked: false, DependsOnRecipe: source.Recipe, FileId: source.Id, LocalId: local.Id));
            }
        }

        foreach (var local in here.Sources.Where(s => !fileNames.Contains(s.Recipe + "|" + s.InputsKey)))
        {
            items.Add(new SetupItem(SetupKind.Clan, "clan:" + local.Recipe + "|" + local.InputsKey, ClanName(local), SetupOutcome.Kept, "only this PC has it", Ticked: false, LocalId: local.Id));
        }

        static string BoardKey(BoardDef b) => "board:" + b.Name.Trim().ToLowerInvariant();

        // First-wins rather than throwing: the Rename window only checks a board against its own old name, so two
        // local boards can fold to the same key (case or spacing) today.
        var hereBoards = new Dictionary<string, BoardDef>(StringComparer.Ordinal);
        foreach (var board in here.SavedBoards.Where(b => b.Follows is null)) hereBoards.TryAdd(BoardKey(board), board);

        var fileBoardKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var board in file.Boards.Where(b => b.Follows is null))
        {
            var boardKey = BoardKey(board);
            fileBoardKeys.Add(boardKey);
            if (hereBoards.TryGetValue(boardKey, out var local))
            {
                items.Add(new SetupItem(SetupKind.Board, boardKey, board.Name, SetupOutcome.Replace,
                    $"{Panels(local.Panels.Count)} here, {board.Panels.Count} in the file", Ticked: true));
            }
            else
            {
                items.Add(new SetupItem(SetupKind.Board, boardKey, board.Name, SetupOutcome.Add, $"{Panels(board.Panels.Count)}", Ticked: true));
            }
        }

        // A saved board the file doesn't carry is still an account of this machine's setup (spec §2).
        foreach (var (boardKey, local) in hereBoards.Where(kv => !fileBoardKeys.Contains(kv.Key)))
        {
            items.Add(new SetupItem(SetupKind.Board, boardKey, local.Name, SetupOutcome.Kept, "only this PC has it", Ticked: false));
        }

        foreach (var key in file.Keys)
        {
            items.Add(new SetupItem(SetupKind.Key, $"key:{key.RecipeSlug}|{key.Id}", key.Label, SetupOutcome.EnterAgain,
                $"{key.RecipeName}. Keys never leave the PC that saved them; Setup › Recipes asks for it.", Ticked: false));
        }

        items.Add(new SetupItem(SetupKind.Stats, "stats", StatsName(readings, finals), SetupOutcome.Add, "what is already here is skipped", Ticked: true));

        return new SetupMergePlan(items, file) { RecipeInstalled = installedHere.Keys.ToHashSet(StringComparer.Ordinal) };
    }

    /// <summary>The state as it arrives (spec §2): every send off, exclusions mapped to this PC's accounts by Roblox id, the unmatched counted.</summary>
    public static RecipeState Arriving(RecipeState fileState, IReadOnlyList<long> excludedUserIds, IReadOnlyList<HostAccount> accounts, out int droppedExclusions)
    {
        var byUserId = AccountMap.Build(accounts);
        var excluded = excludedUserIds.Where(byUserId.ContainsKey).Select(id => byUserId[id].ToString()).ToList();
        droppedExclusions = excludedUserIds.Count - excluded.Count;
        return fileState with
        {
            Stats = fileState.Stats?.ToDictionary(kv => kv.Key, kv => kv.Value with { Send = false }, StringComparer.Ordinal),
            ExcludedAccountIds = excluded.Count == 0 ? null : excluded,
        };
    }

    public static string StatsName(int readings, int finals) =>
        $"{(readings == 1 ? "1 reading" : readings.ToString("N0", CultureInfo.InvariantCulture) + " readings")} and "
        + (finals == 1 ? "1 finished battle" : finals.ToString("N0", CultureInfo.InvariantCulture) + " finished battles");

    private static string ClanName(Source source) => source.Inputs.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? source.Recipe;

    private static string RoleWord(Source source) => source.Role switch { SourceRole.Main => "main", SourceRole.Mine => "yours", _ => "watched" };

    private static string Panels(int count) => count == 1 ? "1 panel" : $"{count} panels";
}
