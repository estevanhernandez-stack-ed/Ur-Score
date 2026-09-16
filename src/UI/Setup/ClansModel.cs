using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One row of a Clans list: the source, its name, and who was found in it.</summary>
public sealed record ClanRow(string SourceId, string Name, SourceRole Role, string Who)
{
    public string Chip => Role switch { SourceRole.Main => "★ main", SourceRole.Mine => "yours", _ => "watching" };

    public bool CanMakeMain => Role == SourceRole.Mine;

    public string MakeMainName => $"Make {Name} main";

    public string RemoveName => $"Remove {Name}";
}

public sealed record ClanLists(ClanRow? Main, IReadOnlyList<ClanRow> Mine, IReadOnlyList<ClanRow> Watching);

/// <summary>What the one read after a pick found, and whether to offer <b>Watch it instead</b>.</summary>
public sealed record ClanProbe(string Text, bool OfferWatch);

/// <summary>The sources after a pick, the picked source's id, and a note when nothing needed adding.</summary>
public sealed record SourceChange(IReadOnlyList<Source> Sources, string SourceId, string? Note);

public sealed record HostRequests(string Host, int PerHour);

/// <summary>
/// Setup › Clans' decisions (spec §7.1, §14), kept out of the page so they are testable. Changes are
/// returned as new source lists; the page saves them through <c>ISetupServices.SaveSources</c>.
/// </summary>
public static class ClansModel
{
    /// <summary>Adding a source past this many for one recipe asks first (spec §14).</summary>
    public const int ConfirmAbove = 5;

    public static string NameOf(Recipe recipe, Source source) =>
        RecipeWords.MainInput(recipe) is { } input && source.Inputs.TryGetValue(input.Id, out var value) && value.Trim().Length > 0
            ? value.Trim()
            : source.Inputs.Count > 0 ? string.Join(" · ", source.Inputs.Values) : recipe.Name;

    /// <summary>Your accounts' names among a read's rows. Never another row's id or name.</summary>
    public static IReadOnlyList<string> FoundAccounts(RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot?.Rows is not { } rows) return [];

        var ids = rows.Select(r => r.UserId).ToHashSet();
        return [.. accounts
            .Where(a => a.RobloxUserId != 0 && ids.Contains(a.RobloxUserId))
            .Select(a => a.DisplayName)
            .Distinct(StringComparer.Ordinal)];
    }

    public static string Who(Recipe recipe, Source source, RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (source.Role == SourceRole.Watch) return $"{RecipeWords.Group(recipe)}-level numbers only";
        if (snapshot?.Rows is null) return "Not read yet";

        var found = FoundAccounts(snapshot, accounts);
        return found.Count == 0 ? "None of your accounts found in the last read" : string.Join(", ", found);
    }

    /// <summary>The main source, the sources your accounts are in (main first), and the watched ones.</summary>
    public static ClanLists Lists(
        Recipe recipe, IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> latest, IReadOnlyList<HostAccount> accounts)
    {
        var own = sources.Where(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal)).ToList();
        ClanRow Row(Source s) => new(s.Id, NameOf(recipe, s), s.Role, Who(recipe, s, latest.GetValueOrDefault(s.Id), accounts));

        var main = own.FirstOrDefault(s => s.Role == SourceRole.Main);
        return new ClanLists(
            main is null ? null : Row(main),
            [.. own.Where(s => s.Role == SourceRole.Main).Concat(own.Where(s => s.Role == SourceRole.Mine)).Select(Row)],
            [.. own.Where(s => s.Role == SourceRole.Watch).Select(Row)]);
    }

    /// <summary>The sentence after the one read that follows a pick (spec §7.1).</summary>
    public static ClanProbe Probe(string name, RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot is null) return new ClanProbe($"Added {name}. It's read when you press Start.", false);

        if (snapshot.Rows is null)
        {
            return new ClanProbe($"Added {name}, but it couldn't be read just now. {snapshot.Detail}".TrimEnd(), false);
        }

        if (accounts.All(a => a.RobloxUserId == 0))
        {
            return new ClanProbe($"Read {name}. RoRoRo hasn't listed your accounts yet, so Ur Score can't say which of them are in it.", false);
        }

        var found = FoundAccounts(snapshot, accounts);
        return found.Count > 0
            ? new ClanProbe($"Found {JoinWithAnd(found)} in {name}.", false)
            : new ClanProbe($"None of your accounts are in {name} yet. You can still watch it.", true);
    }

    /// <summary>
    /// Adds a picked name with a role, or reuses the source that already has it (names match ignoring case,
    /// through <see cref="Source.KeyOf"/>). A new main is added as mine and then made main, so there is only
    /// ever one main per recipe.
    /// </summary>
    public static SourceChange Pick(IReadOnlyList<Source> sources, Recipe recipe, string value, SourceRole role)
    {
        var name = value.Trim();
        var input = RecipeWords.MainInput(recipe) ?? throw new InvalidOperationException($"{recipe.Name} has no input to pick a value for.");
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal) { [input.Id] = name };
        var key = Source.KeyOf(inputs);

        bool Same(Source s) =>
            string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal) && string.Equals(s.InputsKey, key, StringComparison.Ordinal);

        if (sources.FirstOrDefault(Same) is { } existing)
        {
            var shown = NameOf(recipe, existing);
            return role switch
            {
                SourceRole.Main when existing.Role == SourceRole.Main =>
                    new SourceChange(sources, existing.Id, $"{shown} is already your main {RecipeWords.Group(recipe)}."),
                SourceRole.Main =>
                    new SourceChange(SourceRules.MakeMain(Replace(sources, existing with { Enabled = true }), existing.Id), existing.Id, null),
                SourceRole.Mine when existing.Role == SourceRole.Watch =>
                    new SourceChange(Replace(sources, existing with { Role = SourceRole.Mine, Enabled = true }), existing.Id, null),
                SourceRole.Watch when existing.Role != SourceRole.Watch =>
                    new SourceChange(sources, existing.Id,
                        $"{shown} is already one of the {RecipeWords.GroupsLower(recipe)} your accounts are in. Remove it there first if you only want to watch it."),
                _ => new SourceChange(sources, existing.Id, $"{shown} is already on this list."),
            };
        }

        var added = SourceRules.Add(sources, recipe.Slug, inputs, role == SourceRole.Main ? SourceRole.Mine : role);
        var id = added.First(Same).Id;
        return role == SourceRole.Main
            ? new SourceChange(SourceRules.MakeMain(added, id), id, null)
            : new SourceChange(added, id, null);
    }

    public static IReadOnlyList<Source> WatchInstead(IReadOnlyList<Source> sources, string sourceId) =>
        [.. sources.Select(s => s.Id == sourceId ? s with { Role = SourceRole.Watch } : s)];

    public static IReadOnlyList<Source> SetEnabled(IReadOnlyList<Source> sources, string sourceId, bool enabled) =>
        [.. sources.Select(s => s.Id == sourceId ? s with { Enabled = enabled } : s)];

    /// <summary>The source of an installed group-list recipe, which the Top switch turns on and off.</summary>
    public static Source? GroupListSource(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed) =>
        sources.FirstOrDefault(s => installed.Any(i => string.Equals(i.Recipe.Slug, s.Recipe, StringComparison.Ordinal) && i.Recipe.IsGroupList));

    public static bool NeedsConfirmation(IReadOnlyList<Source> sources, string recipeSlug) =>
        sources.Count(s => s.Enabled && string.Equals(s.Recipe, recipeSlug, StringComparison.Ordinal)) >= ConfirmAbove;

    /// <summary>Requests an hour per host: every step once a cycle, a per-account step once per account.</summary>
    public static IReadOnlyList<HostRequests> RequestsPerHour(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, int accountCount)
    {
        var perHost = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var source in sources.Where(s => s.Enabled))
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;

            var cycles = 3600.0 / recipe.EffectiveEverySeconds;
            foreach (var step in recipe.Steps)
            {
                var host = RecipeHosts.HostOf(step.Url);
                perHost.TryGetValue(host, out var sofar);
                perHost[host] = sofar + cycles * (step.PerAccount ? accountCount : 1);
            }
        }

        return [.. perHost.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new HostRequests(kv.Key, (int)Math.Round(kv.Value)))];
    }

    public static string RequestsLine(IReadOnlyList<HostRequests> requests) =>
        string.Join(" ", requests.Select(r => $"Your PC asks {r.Host} about {r.PerHour.ToString("N0", CultureInfo.InvariantCulture)} times an hour."));

    public static string ConfirmText(Recipe recipe, IReadOnlyList<HostRequests> after) =>
        $"That makes more than {ConfirmAbove} {RecipeWords.GroupsLower(recipe)} for {recipe.Name}. {RequestsLine(after)} Add it anyway?"
            .Replace("  ", " ", StringComparison.Ordinal);

    /// <summary>
    /// What a pick asks before it is saved, or null when it needs no asking — a source already on the list, or a
    /// recipe still under <see cref="ConfirmAbove"/> groups. Null means save it straight away; nothing is written
    /// by this either way.
    /// </summary>
    public static Confirm? AddQuestion(
        IReadOnlyList<Source> before, SourceChange change, Recipe recipe,
        IReadOnlyList<InstalledRecipe> installed, int accountCount, string picked)
    {
        if (before.Any(s => s.Id == change.SourceId) || !NeedsConfirmation(before, recipe.Slug)) return null;

        var after = RequestsPerHour(change.Sources, installed, accountCount);
        var group = RecipeWords.Group(recipe);
        return new Confirm($"Add another {group}", ConfirmText(recipe, after), "Add anyway", $"Add {picked} anyway");
    }

    private static IReadOnlyList<Source> Replace(IReadOnlyList<Source> sources, Source updated) =>
        [.. sources.Select(s => s.Id == updated.Id ? updated : s)];

    private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
