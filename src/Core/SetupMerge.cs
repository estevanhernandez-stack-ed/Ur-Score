using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Labs626.UrScore.Board;
using Labs626.UrScore.Games;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

public enum SetupKind { Recipe, Clan, Board, Key, Stats, Mode }

public enum SetupOutcome { Add, Update, Same, Replace, Kept, EnterAgain, Skipped }

/// <summary>
/// One line of the preview: what kind of thing, its name, what importing would do to it and why, whether it starts
/// ticked, and — for a clan — the recipe it needs. <see cref="FileId"/> and <see cref="LocalId"/> are a clan's ids on
/// each side; an Add has no local id yet, and Apply mints one.
/// </summary>
public sealed record SetupItem(
    SetupKind Kind, string Key, string Name, SetupOutcome Outcome, string Note, bool Ticked,
    string? DependsOnRecipe = null, string? FileId = null, string? LocalId = null);

/// <summary>
/// This machine's setup, as the plan needs it. <see cref="Settings"/> is THIS PC's whole settings record — the
/// settings step lays the file's setting and ticked mode switches over it rather than writing the file's record, so
/// <c>StartOnOpen</c>, which never travels, stays what this machine chose. <see cref="Catalog"/> says which readers
/// belong to a mode (null: the built-in one); a reader no mode names is skipped, not merged.
/// </summary>
public sealed record SetupHere(
    IReadOnlyList<InstalledRecipe> Installed, IReadOnlyList<Source> Sources, IReadOnlyList<BoardDef> SavedBoards, IReadOnlyList<HostAccount> Accounts,
    Settings Settings, GameCatalog? Catalog = null);

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

/// <summary>The stores <see cref="SetupMerge.Apply"/> writes through, so the apply is testable against a fake and the app's own writers stay the only writers.</summary>
public interface ISetupWriter
{
    /// <summary>The data folder, for the aside copy beside it.</summary>
    string DataRoot { get; }

    SetupHere Here { get; }

    /// <summary>One reader's choices (A7): the reader's text is the app's own and is never written by an import.</summary>
    void SaveState(string slug, RecipeState state);

    void SaveSources(IReadOnlyList<Source> sources);

    /// <summary>The whole saved boards list, replaced: sanitized and written once, then redrawn.</summary>
    void SaveImportedBoards(IReadOnlyList<BoardDef> saved);

    void SaveSettings(Settings settings);
}

/// <summary>
/// What Apply did, for the line the page says. <see cref="Recipes"/> counts the readers whose ticks were written and
/// <see cref="Modes"/> the modes those readers belong to, which is what the line counts (review round 2).
/// <see cref="FailedStep"/> names the step that threw, or null.
/// <see cref="SkippedItems"/> counts the items left out because no mode reads their reader (A7), and
/// <see cref="ModesApplied"/> says the file's mode switches were applied. <see cref="FailureType"/> is for
/// the trail; <see cref="FailureMessage"/> is what the failure said, redacted, for the screen (spec §4).
/// </summary>
public sealed record SetupApplied(
    int Recipes, int Clans, int Boards, int KeptClans, int Keys, int DroppedExclusions, string? AsideFolder, string? FailedStep, string? FailureType,
    int SkippedItems = 0, string? FailureMessage = null, bool ModesApplied = false, int Modes = 0);

/// <summary>
/// Importing a setup: the plan, pure (spec §2), and the apply (spec §4, Task 6). Identity: a reader by slug, a clan by
/// recipe and clan name (case and spaces aside, as the book import matches), a board by name. The file wins where
/// both have a thing and it differs; a clan keeps THIS machine's id; sends arrive off.
/// </summary>
public static class SetupMerge
{
    public static SetupMergePlan Plan(SetupPack file, SetupHere here, int readings, int finals)
    {
        var items = new List<SetupItem>();
        var installedHere = here.Installed.ToDictionary(i => i.Recipe.Slug, StringComparer.Ordinal);

        var catalog = here.Catalog ?? GameCatalog.BuiltIn;

        // A reader's ticks merge by STATE, never by text (A7): both PCs carry the same embedded text, so text says
        // nothing, and the file's text (an old export's) is not even read. A reader no mode names is skipped.
        var userIdOf = here.Accounts.Where(a => a.RobloxUserId != 0).ToDictionary(a => a.AccountId, a => a.RobloxUserId);
        foreach (var recipe in file.Recipes)
        {
            var key = "recipe:" + recipe.Slug;
            var name = ReaderNames.For(recipe.Slug, catalog, here.Installed);
            if (catalog.ModeOf(recipe.Slug) is not { } mode || !installedHere.TryGetValue(recipe.Slug, out var local))
            {
                items.Add(new SetupItem(SetupKind.Recipe, key, name, SetupOutcome.Skipped, NotInAMode, Ticked: false));
                continue;
            }

            var theirs = SetupPack.Canonical(RecipeStates.Effective(local.Recipe, recipe.State with { CounterNames = null }, Readers.ShowsFor(mode, recipe.Slug)));
            var mine = SetupPack.Canonical(local.State with { CounterNames = null, ExcludedAccountIds = null });
            var sameExcluded = local.State.Excluded.Where(userIdOf.ContainsKey).Select(id => userIdOf[id]).Order().SequenceEqual(recipe.ExcludedUserIds.Order());
            items.Add(string.Equals(theirs, mine, StringComparison.Ordinal) && sameExcluded
                ? new SetupItem(SetupKind.Recipe, key, name, SetupOutcome.Same, "same as here", Ticked: false)
                : new SetupItem(SetupKind.Recipe, key, name, SetupOutcome.Update, "the file's ticks differ from here; they come with it, sends off", Ticked: true));
        }

        // Which readers a ticked state could name: a clan is named by its reader's mode when it has no input.
        var recipeNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var recipe in file.Recipes) recipeNames.TryAdd(recipe.Slug, ReaderNames.For(recipe.Slug, catalog, here.Installed));
        foreach (var installed in here.Installed) recipeNames.TryAdd(installed.Recipe.Slug, ReaderNames.For(installed.Recipe.Slug, catalog, here.Installed));

        var fileNames = file.Sources.Select(s => s.Recipe + "|" + s.InputsKey).ToHashSet(StringComparer.Ordinal);
        foreach (var source in file.Sources)
        {
            var identity = source.Recipe + "|" + source.InputsKey;
            var name = ClanName(source, recipeNames);
            var local = here.Sources.FirstOrDefault(s => string.Equals(s.Recipe + "|" + s.InputsKey, identity, StringComparison.Ordinal));
            if (catalog.ModeOf(source.Recipe) is null)
            {
                // Skipped with its reader: a clan of a reader no mode names has nothing here to read it.
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Skipped, NotInAMode, Ticked: false, FileId: source.Id));
            }
            else if (local is null)
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
            items.Add(new SetupItem(SetupKind.Clan, "clan:" + local.Recipe + "|" + local.InputsKey, ClanName(local, recipeNames), SetupOutcome.Kept, "only this PC has it", Ticked: false, LocalId: local.Id));
        }

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
                $"{ReaderNames.For(key.RecipeSlug, catalog, here.Installed)}. Keys never leave the PC that saved them.", Ticked: false));
        }

        if (file.ModesUnreadable)
        {
            items.Add(new SetupItem(SetupKind.Mode, "modes", "Mode switches", SetupOutcome.Skipped, "the file's mode switches could not be read", Ticked: false));
        }
        else if (ModeChanges(file.Settings.Modes, here.Settings.Modes, catalog) is { } changes)
        {
            items.Add(changes.Count == 0
                ? new SetupItem(SetupKind.Mode, "modes", "Mode switches", SetupOutcome.Same, "same as here", Ticked: false)
                : new SetupItem(SetupKind.Mode, "modes", "Mode switches", SetupOutcome.Update, "the file sets " + string.Join(", ", changes), Ticked: true));
        }

        items.Add(new SetupItem(SetupKind.Stats, "stats", StatsName(readings, finals), SetupOutcome.Add, "what is already here is skipped", Ticked: true));

        return new SetupMergePlan(items, file) { RecipeInstalled = installedHere.Keys.ToHashSet(StringComparer.Ordinal) };
    }

    private const string NotInAMode = "not part of any mode";

    /// <summary>
    /// What the file's mode switches would change here, as "Battle off" words: only the keys the catalog knows, and
    /// only where the EFFECTIVE value here differs (an explicit true over a default true changes nothing). Null when the
    /// file carries no switches at all, so no item is offered.
    /// </summary>
    private static List<string>? ModeChanges(IReadOnlyDictionary<string, bool>? file, IReadOnlyDictionary<string, bool>? here, GameCatalog catalog)
    {
        if (file is not { Count: > 0 }) return null;

        var switches = new ModeSwitches(catalog, here);
        var changes = new List<string>();
        foreach (var (key, on) in file.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (catalog.Find(key) is { } mode)
            {
                if (switches.IsModeSet(key) != on) changes.Add($"{mode.Name} {(on ? "on" : "off")}");
            }
            else if (catalog.Games.FirstOrDefault(g => string.Equals(g.Id, key, StringComparison.Ordinal)) is { } game && switches.IsGameOn(key) != on)
            {
                changes.Add($"{game.Name} {(on ? "on" : "off")}");
            }
        }

        return changes;
    }

    /// <summary>
    /// Applies the ticked items in dependency order (spec §4): the aside copy first, then reader ticks, clans,
    /// boards, settings (and the mode switches with them, live). Each step is atomic on its own file; a step that throws ends the apply with its name and
    /// the exception's TYPE, and the steps before it stand. No roll-back: the aside folder is the recovery.
    /// <para>
    /// The catch takes anything but a cancellation, rather than a list of types: the call graph here runs through
    /// the real stores, a JSON serializer and the file system, so the list was narrower than what can actually
    /// arrive — and a named step with its aside folder is a better end for a person than an unhandled throw
    /// becoming the page's generic "That file could not be imported" (final review, 2026-09-22).
    /// </para>
    /// </summary>
    public static SetupApplied Apply(SetupMergePlan plan, IReadOnlySet<string> tickedKeys, ISetupWriter writer, DateTimeOffset now)
    {
        var here = writer.Here;
        var ticked = plan.Ticked(tickedKeys).ToList();
        var recipes = 0;
        var clans = 0;
        var boards = 0;
        var dropped = 0;
        // Only the skips the line explains ("not part of any mode"); an unreadable modes file is its own item with its own words.
        var skipped = plan.Items.Count(i => i.Outcome == SetupOutcome.Skipped && i.Note == NotInAMode);
        var modesApplied = false;
        var modesTouched = new HashSet<string>(StringComparer.Ordinal);
        var catalog = here.Catalog ?? GameCatalog.BuiltIn;
        string? aside = null;
        var step = "aside";
        try
        {
            aside = AsideFolder(writer.DataRoot, now);   // named before the copy runs, so a failure mid-copy still reports it
            CopyAside(writer.DataRoot, aside);

            step = "recipes";
            var fileRecipes = plan.File.Recipes.ToDictionary(r => r.Slug, StringComparer.Ordinal);
            foreach (var item in ticked.Where(i => i.Kind == SetupKind.Recipe))
            {
                var recipe = fileRecipes[item.Key["recipe:".Length..]];
                var local = here.Installed.First(i => i.Recipe.Slug == recipe.Slug);
                var state = Arriving(recipe.State, recipe.ExcludedUserIds, here.Accounts, out var droppedHere);
                dropped += droppedHere;
                // Counter names are what the last read saw on THIS PC, not a choice, so the file's absence keeps them.
                writer.SaveState(recipe.Slug, state with { CounterNames = state.CounterNames ?? local.State.CounterNames });
                if (catalog.ModeOf(recipe.Slug) is { } mode) modesTouched.Add(mode.Key);
                recipes++;
            }

            step = "clans";
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var sources = here.Sources.ToList();
            foreach (var item in plan.Items.Where(i => i.Kind == SetupKind.Clan && i.FileId is not null && i.LocalId is not null))
            {
                idMap[item.FileId!] = item.LocalId!;   // Same and Replace: the file's id means the local clan
            }

            foreach (var item in ticked.Where(i => i.Kind == SetupKind.Clan))
            {
                var fromFile = plan.File.Sources.First(s => s.Id == item.FileId);
                string localId;
                if (item.Outcome == SetupOutcome.Replace)
                {
                    var at = sources.FindIndex(s => s.Id == item.LocalId);
                    if (at < 0) throw new InvalidOperationException($"the clan \"{item.Name}\" is no longer here to replace");
                    localId = item.LocalId!;
                    sources[at] = fromFile with { Id = localId };
                }
                else
                {
                    localId = SourceRules.NewId();
                    idMap[fromFile.Id] = localId;
                    sources.Add(fromFile with { Id = localId });
                }

                // The arriving Main wins: at most one per recipe (Sources.cs), so an arriving Main demotes
                // any other Main on the same recipe to Mine, the same rule SourceRules.Add already enforces.
                if (fromFile.Role == SourceRole.Main) sources = SourceRules.MakeMain(sources, localId).ToList();

                clans++;
            }

            if (clans > 0) writer.SaveSources(sources);

            step = "boards";
            var tickedBoards = ticked.Where(i => i.Kind == SetupKind.Board).Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
            if (tickedBoards.Count > 0)
            {
                var saved = here.SavedBoards.ToList();
                foreach (var board in plan.File.Boards.Where(b => b.Follows is null && tickedBoards.Contains(BoardKey(b))))
                {
                    var rewritten = Rewrite(board, idMap);
                    var at = saved.FindIndex(b => b.Follows is null && BoardKey(b) == BoardKey(board));
                    if (at >= 0) saved[at] = rewritten with { Id = saved[at].Id };
                    else saved.Add(rewritten);
                    boards++;
                }

                writer.SaveImportedBoards(saved);
            }

            step = "settings";
            // The one setting that travels, and the mode switches when their item is ticked, laid over this PC's own
            // record. The file's record is NOT written: SetupPack.FromFolder rebuilds it with StartOnOpen at the record
            // default, and saving it would turn this machine's choice off on every import. ActiveRecipe is never
            // written any more (A7). SaveSettings applies the switches live.
            var modes = here.Settings.Modes;
            if (ticked.Any(i => i.Kind == SetupKind.Mode) && plan.File.Settings.Modes is { } fileModes)
            {
                var merged = new Dictionary<string, bool>(modes ?? new Dictionary<string, bool>(), StringComparer.Ordinal);
                foreach (var (key, on) in fileModes)
                {
                    if (catalog.Find(key) is not null || catalog.Games.Any(g => string.Equals(g.Id, key, StringComparison.Ordinal))) merged[key] = on;
                }

                modes = merged;
                modesApplied = true;
            }

            writer.SaveSettings(here.Settings with { ResolveNames = plan.File.Settings.ResolveNames, Modes = modes });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SetupApplied(recipes, clans, boards, KeptClans(plan), plan.File.Keys.Count, dropped, aside, step, ex.GetType().Name,
                skipped, Redact(ex.Message, writer.DataRoot), modesApplied, modesTouched.Count);
        }

        return new SetupApplied(recipes, clans, boards, KeptClans(plan), plan.File.Keys.Count, dropped, aside, null, null,
            skipped, ModesApplied: modesApplied, Modes: modesTouched.Count);
    }

    /// <summary>
    /// A failure's own words with every path at or beside the data folder cut back to its file name, so the line on
    /// screen says WHAT could not be written without saying where a person's files live. The aside folder matches
    /// too: it is the data folder's own name plus a stamp, so it shares the prefix.
    /// <para>
    /// The run is taken up to the next quote rather than the next space, because a BCL file-IO message quotes its
    /// path ("Access to the path 'X' is denied.") and because erring long errs towards saying too little, which is
    /// the safe direction for a privacy rule. Key VALUES are <see cref="Recipes.Redactor"/>'s job and not this
    /// method's — it is pure core and cannot reach the key store — so the page runs the line this message ends up
    /// in through that as well, and the two halves together are what "redacted" means here.
    /// </para>
    /// </summary>
    internal static string Redact(string message, string dataRoot)
    {
        var root = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.Length == 0 || message.Length == 0) return message;

        return Regex.Replace(
            message,
            Regex.Escape(root) + "[^'\"\r\n]*",
            found => Path.GetFileName(found.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
                ? name
                : "the data folder",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));
    }

    private static int KeptClans(SetupMergePlan plan) => plan.Items.Count(i => i.Kind == SetupKind.Clan && i.Outcome == SetupOutcome.Kept);

    /// <summary>A board's identity: its name, case and spaces aside — the one place both <see cref="Plan"/> and <see cref="Apply"/> fold it.</summary>
    private static string BoardKey(BoardDef b) => "board:" + b.Name.Trim().ToLowerInvariant();

    /// <summary>A board from the file with every clan id it points at mapped to this machine's; an unmapped id is left as it is, which points at nothing here.</summary>
    private static BoardDef Rewrite(BoardDef board, IReadOnlyDictionary<string, string> idMap) =>
        board with
        {
            Panels = [.. board.Panels.Select(p => p with
            {
                Settings = p.Settings with
                {
                    SourceId = p.Settings.SourceId is { } one ? idMap.GetValueOrDefault(one, one) : null,
                    SourceIds = p.Settings.SourceIds?.Select(id => idMap.GetValueOrDefault(id, id)).ToList(),
                    ToSourceId = p.Settings.ToSourceId is { } to ? idMap.GetValueOrDefault(to, to) : null,
                },
            })],
        };

    /// <summary>
    /// A fresh, empty aside folder beside the data folder, dated to the minute. Two imports in the same minute
    /// never share one: a second (third, ...) gets <c>-2</c>, <c>-3</c>, ... rather than overwriting the first,
    /// which is the only recovery there is. Named and created before anything is copied into it, so a copy that
    /// throws still leaves a folder Apply can report.
    /// </summary>
    private static string AsideFolder(string dataRoot, DateTimeOffset now)
    {
        var stamp = now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var baseName = $"{dataRoot.TrimEnd(Path.DirectorySeparatorChar)}.before-import-{stamp}";
        var folder = baseName;
        for (var n = 2; Directory.Exists(folder); n++) folder = $"{baseName}-{n}";

        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>The setup's files copied into the aside folder, so a person can put them back by hand.</summary>
    private static void CopyAside(string dataRoot, string folder)
    {
        foreach (var name in new[] { "sources.json", "boards.json", "settings.json" })
        {
            var file = Path.Combine(dataRoot, name);
            if (File.Exists(file)) File.Copy(file, Path.Combine(folder, name), overwrite: true);
        }

        var recipes = Path.Combine(dataRoot, "recipes");
        if (Directory.Exists(recipes))
        {
            Directory.CreateDirectory(Path.Combine(folder, "recipes"));
            foreach (var file in Directory.EnumerateFiles(recipes)) File.Copy(file, Path.Combine(folder, "recipes", Path.GetFileName(file)), overwrite: true);
        }
    }

    /// <summary>
    /// The state as it arrives (spec §2): every send off, exclusions mapped to this PC's accounts by Roblox id, the
    /// unmatched counted.
    /// <para>
    /// <see cref="RecipeState"/> holds TWO send lists, and both are cleared here.
    /// <see cref="RecipeState.Stats"/>'s per-stat <see cref="StatChoice.Send"/> is the per-account one;
    /// <see cref="RecipeState.SentFieldMetrics"/> is a clans list's clan-and-field numbers, which
    /// <c>AppServices.PolicyFor</c> hands to every <c>ReportPolicy</c> OUTSIDE the role gate — so a clans-list
    /// recipe arriving with that list intact would start reporting your clan's standing to the receiving PC's
    /// RoRoRo with nothing ticked (final review, 2026-09-22).
    /// </para>
    /// </summary>
    public static RecipeState Arriving(RecipeState fileState, IReadOnlyList<long> excludedUserIds, IReadOnlyList<HostAccount> accounts, out int droppedExclusions)
    {
        var byUserId = AccountMap.Build(accounts);
        var excluded = excludedUserIds.Where(byUserId.ContainsKey).Select(id => byUserId[id].ToString()).ToList();
        droppedExclusions = excludedUserIds.Count - excluded.Count;
        return fileState with
        {
            Stats = fileState.Stats?.ToDictionary(kv => kv.Key, kv => kv.Value with { Send = false }, StringComparer.Ordinal),
            ExcludedAccountIds = excluded.Count == 0 ? null : excluded,
            SentFieldMetrics = null,
        };
    }

    public static string StatsName(int readings, int finals) =>
        $"{(readings == 1 ? "1 reading" : readings.ToString("N0", CultureInfo.InvariantCulture) + " readings")} and "
        + (finals == 1 ? "1 finished battle" : finals.ToString("N0", CultureInfo.InvariantCulture) + " finished battles");

    private static string ClanName(Source source, IReadOnlyDictionary<string, string> recipeNames) =>
        source.Inputs.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim()
        ?? recipeNames.GetValueOrDefault(source.Recipe)
        ?? source.Recipe;

    private static string RoleWord(Source source) => source.Role switch { SourceRole.Main => "main", SourceRole.Mine => "yours", _ => "watched" };

    private static string Panels(int count) => count == 1 ? "1 panel" : $"{count} panels";
}
