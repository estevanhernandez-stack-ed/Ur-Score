using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>One thing a panel's form can ask for.</summary>
public enum PanelField { Source, Sources, ToSource, Stat, Account }

/// <summary>One entry in a form's list. Its key is a source id, a stat key (<see cref="PanelForms.StatKey"/>) or an account id.</summary>
public sealed record FormChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// What one list in a form shows for a value: the choice picked, or none. <paramref name="Held"/> is a saved value the
/// list no longer offers, which the form keeps checking until you pick another, so it is never blanked silently.
/// </summary>
public sealed record FormPick(FormChoice? Selected, string? Held);

/// <summary>What a form holds before it becomes <see cref="PanelSettings"/>.</summary>
public sealed record FormValues(string? Source = null, IReadOnlyList<string>? Sources = null, string? ToSource = null, string? Stat = null, string? Account = null);

/// <summary>
/// What each panel asks for, offers and refuses (spec §9.4, R14, R15). Pure. Adding asks for the "Needs"
/// column only; the rest is defaulted and shows in ⋯ settings.
/// </summary>
public static class PanelForms
{
    public const char KeySeparator = (char)0x1F;

    public const string TopAccountKey = "";

    public const string NoStatKey = "";

    private const string ChooseAnother = "Choose another.";

    public static string StatKey(string recipe, string stat) => recipe + KeySeparator + stat;

    public static (string Recipe, string Stat)? SplitStatKey(string? key)
    {
        if (key is null) return null;

        var at = key.IndexOf(KeySeparator);
        return at <= 0 || at >= key.Length - 1 ? null : (key[..at], key[(at + 1)..]);
    }

    /// <summary>Whether a recipe's sources or stats can feed this panel type.</summary>
    public static bool Fits(PanelType type, Recipe recipe) => type switch
    {
        PanelType.Top => recipe.IsGroupList,
        _ when recipe.IsGroupList => false,
        PanelType.Standing => recipe.Headline.Count > 0,
        PanelType.Race => recipe.Headline.Any(h => h.Sum),
        PanelType.PromotionCheck or PanelType.LiveLeaderboard => !recipe.LastStep.PerAccount,
        PanelType.PastPeriods => recipe.Period?.Past is not null,
        PanelType.ProfileStat or PanelType.AccountsTable => recipe.Period is null,
        _ => true,
    };

    public static IReadOnlyList<PanelField> Fields(PanelType type, bool adding)
    {
        var needs = type switch
        {
            PanelType.Race => new[] { PanelField.Sources },
            PanelType.PromotionCheck => new[] { PanelField.Source, PanelField.ToSource },
            PanelType.AccountCard => new[] { PanelField.Account },
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat => new[] { PanelField.Stat },
            _ => new[] { PanelField.Source },
        };
        if (adding) return needs;

        var extra = type switch
        {
            PanelType.PromotionCheck or PanelType.AccountCard or PanelType.PastPeriods => new[] { PanelField.Stat },
            PanelType.ProfileStat => new[] { PanelField.Source },
            _ => Array.Empty<PanelField>(),
        };
        return [.. needs, .. extra];
    }

    /// <summary>
    /// The fields a form shows for these values: <see cref="Fields(PanelType, bool)"/>, except that adding a Profile stat
    /// also shows its source when the stat's recipe has more than one, or only one that is off, so the source saved is
    /// always one you saw (R14). With one source that is on, that one is saved.
    /// </summary>
    public static IReadOnlyList<PanelField> Fields(PanelType type, bool adding, LiveBoard live, FormValues values)
    {
        var fields = Fields(type, adding);
        if (type != PanelType.ProfileStat || fields.Contains(PanelField.Source)) return fields;

        var sources = SourceChoices(type, PanelField.Source, live, values).Select(c => live.FindSource(c.Key)).ToList();
        return sources.Count > 1 || sources.Any(s => s?.Enabled != true) ? [.. fields, PanelField.Source] : fields;
    }

    /// <summary>
    /// What a list shows for <paramref name="key"/>: that choice while it is offered. Else a hidden field takes its first
    /// choice; a shown one shows none, and holds the key when it is the <paramref name="saved"/> value, so the form's
    /// problem line names what is wrong with it (a removed clan, an account not listed) instead of saving a default.
    /// </summary>
    public static FormPick Pick(IReadOnlyList<FormChoice> choices, string? key, bool shown, string? saved)
    {
        if (choices.FirstOrDefault(c => c.Key == key) is { } offered) return new FormPick(offered, null);
        if (!shown) return new FormPick(choices.FirstOrDefault(), null);

        return new FormPick(null, key is not null && key == saved ? key : null);
    }

    /// <summary>Sources that fit, main first, then yours, then watched. Promotion check's "to" is another source of the "from" recipe.</summary>
    public static IReadOnlyList<FormChoice> SourceChoices(PanelType type, PanelField field, LiveBoard live, FormValues values)
    {
        var fitting = live.Sources.Where(s => live.FindRecipe(s.Recipe) is { } installed && Fits(type, installed.Recipe));

        if (type == PanelType.PromotionCheck)
        {
            var origin = live.FindSource(values.Source);
            fitting = field == PanelField.ToSource
                ? fitting.Where(s => origin is not null && s.Recipe == origin.Recipe && s.Id != origin.Id)
                : fitting.Where(s => s.Role != SourceRole.Watch);
        }
        else if (type == PanelType.ProfileStat)
        {
            var recipe = SplitStatKey(values.Stat)?.Recipe;
            fitting = fitting.Where(s => s.Recipe == recipe);
        }

        var list = fitting.OrderBy(s => s.Role).ToList();
        var severalRecipes = list.Select(s => s.Recipe).Distinct(StringComparer.Ordinal).Count() > 1;
        return [.. list.Select(s => new FormChoice(s.Id, SourceLabel(live, s, severalRecipes)))];
    }

    /// <summary>Ticked stats (only they are read), plus the panel's current stat while its recipe still offers it.</summary>
    public static IReadOnlyList<FormChoice> StatChoices(PanelType type, LiveBoard live, FormValues values, PanelSettings? current)
    {
        string? only = null;
        if (type is PanelType.PromotionCheck or PanelType.PastPeriods)
        {
            only = live.FindSource(values.Source)?.Recipe;
            if (only is null) return [];
        }

        var found = new List<(InstalledRecipe Installed, List<RecipeStat> Stats)>();
        foreach (var installed in live.Installed.Where(i => Fits(type, i.Recipe) && (only is null || i.Recipe.Slug == only)))
        {
            var tracked = installed.State.TrackedStats(installed.Recipe);
            var keep = current is { Stat: { } stat } && current.Recipe == installed.Recipe.Slug ? stat : null;
            var stats = RecipeStats.Offered(installed.Recipe, installed.State.StatChoices.Keys)
                .Where(s => tracked.Contains(s.Key) || s.Key == keep)
                .ToList();
            if (stats.Count > 0) found.Add((installed, stats));
        }

        var choices = found.SelectMany(f => f.Stats.Select(s => new FormChoice(
            StatKey(f.Installed.Recipe.Slug, s.Key),
            found.Count > 1 ? $"{s.Label} · {f.Installed.Recipe.Name}" : s.Label))).ToList();
        if (type == PanelType.PastPeriods && live.FindRecipe(only)?.Recipe is { } recipe && Fits(type, recipe))
        {
            choices.Add(new FormChoice(NoStatKey, "Don't show your best account"));
        }

        return choices;
    }

    /// <summary>"Your top account", then your accounts by name. Never anyone else's.</summary>
    public static IReadOnlyList<FormChoice> AccountChoices(LiveBoard live) =>
    [
        new FormChoice(TopAccountKey, "Your top account"),
        .. live.Accounts
            .Where(a => a.RobloxUserId != 0)
            .DistinctBy(a => a.RobloxUserId)
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new FormChoice(a.RobloxUserId.ToString(CultureInfo.InvariantCulture), a.DisplayName)),
    ];

    /// <summary>What a new panel's form starts with: the main, the main's other sources, the first ticked stat (§9.4 "default: → main").</summary>
    public static FormValues Defaults(PanelType type, LiveBoard live)
    {
        var none = new FormValues();
        switch (type)
        {
            case PanelType.Race:
            {
                var fitting = SourceChoices(type, PanelField.Sources, live, none)
                    .Select(c => live.FindSource(c.Key))
                    .OfType<Source>()
                    .Where(s => s.Enabled)
                    .ToList();
                var recipe = fitting.FirstOrDefault()?.Recipe;
                return new FormValues(Sources: fitting.Where(s => s.Recipe == recipe).Take(PanelModels.MaxRace).Select(s => s.Id).ToList());
            }

            case PanelType.PromotionCheck:
            {
                var main = live.Sources.FirstOrDefault(s =>
                    s.Enabled && s.Role == SourceRole.Main && live.FindRecipe(s.Recipe) is { } installed && Fits(type, installed.Recipe));
                var origin = main is null ? null : live.Sources.FirstOrDefault(s => s.Enabled && s.Role == SourceRole.Mine && s.Recipe == main.Recipe);
                var values = new FormValues(Source: origin?.Id, ToSource: main?.Id);
                return values with { Stat = StatChoices(type, live, values, null).FirstOrDefault()?.Key };
            }

            case PanelType.AccountCard:
                return new FormValues(Account: TopAccountKey, Stat: StatChoices(type, live, none, null).FirstOrDefault()?.Key);

            case PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat:
                return new FormValues(Stat: StatChoices(type, live, none, null).FirstOrDefault()?.Key);

            default:
            {
                var source = SourceChoices(type, PanelField.Source, live, none).FirstOrDefault(c => live.FindSource(c.Key)?.Enabled == true)?.Key;
                var values = new FormValues(Source: source);
                return type == PanelType.PastPeriods ? values with { Stat = StatChoices(type, live, values, null).FirstOrDefault()?.Key } : values;
            }
        }
    }

    /// <summary>A saved panel's settings as form values, for ⋯ settings.</summary>
    public static FormValues From(PanelSettings settings, PanelType? type = null) => new(
        settings.SourceId,
        settings.SourceIds,
        settings.ToSourceId,
        settings.Stat is { } stat && settings.Recipe.Length > 0 ? StatKey(settings.Recipe, stat) : type == PanelType.PastPeriods ? NoStatKey : null,
        settings.UserId is { } id ? id.ToString(CultureInfo.InvariantCulture) : TopAccountKey);

    /// <summary>
    /// The settings a panel type reads (stage 1's <see cref="PanelModels"/>). The recipe comes from the source
    /// (or the first raced source) for source panels and from the stat for stat panels; a stat from another
    /// recipe than the source's is dropped.
    /// </summary>
    public static PanelSettings Build(PanelType type, FormValues values, LiveBoard live)
    {
        var stat = SplitStatKey(values.Stat);
        var source = live.FindSource(values.Source);
        var raced = (values.Sources ?? Array.Empty<string>()).Select(live.FindSource).OfType<Source>().ToList();

        var recipe = type switch
        {
            PanelType.Race => raced.FirstOrDefault()?.Recipe,
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat or PanelType.AccountCard => stat?.Recipe,
            _ => source?.Recipe,
        } ?? "";

        var sourceId = type switch
        {
            // The recipe's first source that is on, else its first source: one that is off still shows, it isn't removed (R16).
            PanelType.ProfileStat => source is not null && source.Recipe == recipe ? source.Id : FirstSourceOfRecipe(live, recipe)?.Id,
            PanelType.Race or PanelType.MyAccounts or PanelType.Records or PanelType.AccountCard => null,
            _ => source?.Id,
        };

        var keepsStat = type is not (PanelType.Standing or PanelType.Race or PanelType.Top or PanelType.LiveLeaderboard or PanelType.AccountsTable);
        long? userId = type == PanelType.AccountCard
                       && long.TryParse(values.Account, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

        return new PanelSettings(
            recipe,
            SourceId: sourceId,
            SourceIds: type == PanelType.Race ? raced.Select(s => s.Id).ToList() : null,
            ToSourceId: type == PanelType.PromotionCheck ? values.ToSource : null,
            Stat: keepsStat && stat is { } picked && picked.Recipe == recipe ? picked.Stat : null,
            UserId: userId);
    }

    /// <summary>What's wrong with a panel's settings, in the recipe's words, or null. The window refuses to save while there is one.</summary>
    public static string? Problem(PanelType type, PanelSettings settings, LiveBoard live, string? fallbackRecipe = null)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var wordingRecipe = string.IsNullOrEmpty(settings.Recipe) ? fallbackRecipe : settings.Recipe;
        var (word, words) = installed is { Recipe.IsGroupList: false }
            ? GroupWords(installed.Recipe)
            : type == PanelType.Top || (installed is null && !string.IsNullOrEmpty(wordingRecipe) && live.FindRecipe(wordingRecipe) is null)
                ? GroupWords((Recipe?)null)
                : GroupWords(live);

        foreach (var field in Fields(type, adding: false))
        {
            var problem = field switch
            {
                PanelField.Source => SourceProblem(type, settings, live, word),
                PanelField.Sources => RaceProblem(settings, live, word, words),
                PanelField.ToSource => ToSourceProblem(settings, live, word),
                PanelField.Stat => StatProblem(type, settings, installed),
                _ => settings.UserId is { } id && !live.MyUserIds.Contains(id) ? "Choose one of your accounts." : null,
            };
            if (problem is not null) return problem;
        }

        return null;
    }

    public static string? Warning(PanelType type, PanelSettings settings, LiveBoard live) =>
        type == PanelType.ProfileStat
        && live.FindSource(settings.SourceId) is { Enabled: false } source
        && source.Recipe == settings.Recipe
            ? PanelText.SwitchedOff(live.SourceName(source))
            : null;

    /// <summary>The saved recipe's words, or the first recipe with inputs when none is picked yet.</summary>
    internal static (string Group, string Groups) GroupWords(LiveBoard live, string? recipe = null) =>
        GroupWords(string.IsNullOrEmpty(recipe) ? PanelText.GroupRecipe(live.Installed) : live.FindRecipe(recipe)?.Recipe);

    /// <summary>A recipe's words for one group and several, lower case: "clan", "clans"; with no recipe, "source", "sources".</summary>
    internal static (string Group, string Groups) GroupWords(Recipe? recipe) =>
        recipe is null ? ("source", "sources") : (RecipeWords.Group(recipe), RecipeWords.GroupsLower(recipe));

    /// <summary>
    /// The recipe's first source that is on, else its first: one that is off still shows, it isn't removed (R16).
    /// The one rule for "the recipe's source": a saved panel's sourceId (<see cref="Build"/>), and what an unpinned Accounts
    /// table (D17) or Profile stat reads.
    /// </summary>
    internal static Source? FirstSourceOfRecipe(LiveBoard live, string recipe) =>
        live.Sources.FirstOrDefault(s => s.Enabled && s.Recipe == recipe) ?? live.Sources.FirstOrDefault(s => s.Recipe == recipe);

    private static string SourceLabel(LiveBoard live, Source source, bool withRecipe)
    {
        var name = live.SourceName(source);
        var label = PanelText.SourceLabel(name, source.Role);

        if (withRecipe && live.FindRecipe(source.Recipe) is { } installed && installed.Recipe.Name != name) label += $" · {installed.Recipe.Name}";
        return source.Enabled ? label : $"{label} · off";
    }

    private static string? SourceProblem(PanelType type, PanelSettings settings, LiveBoard live, string word)
    {
        // A Profile stat or an Accounts table with no source reads the recipe's first source that is on; with none on, it must pin one.
        if (settings.SourceId is null)
        {
            return type is PanelType.ProfileStat or PanelType.AccountsTable && live.Sources.Any(s => s.Enabled && s.Recipe == settings.Recipe) ? null : $"Choose a {word}.";
        }

        if (live.FindSource(settings.SourceId) is not { } source) return $"{PanelText.StaleSource(word)} {ChooseAnother}";
        if (source.Recipe != settings.Recipe || live.FindRecipe(source.Recipe) is not { } installed || !Fits(type, installed.Recipe))
        {
            return $"This panel can't show that {word}.";
        }

        return type == PanelType.PromotionCheck && source.Role == SourceRole.Watch ? $"Choose a {word} your accounts are in." : null;
    }

    private static string? RaceProblem(PanelSettings settings, LiveBoard live, string word, string words)
    {
        var ids = settings.SourceIds ?? Array.Empty<string>();
        if (ids.Count < 2 || ids.Count > PanelModels.MaxRace) return $"Choose 2 to {PanelModels.MaxRace} {words}.";
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) return $"Choose each {word} once.";

        var sources = ids.Select(live.FindSource).ToList();
        if (sources.Any(s => s is null)) return $"One of this race's {words} was removed. Choose another.";
        if (sources.Any(s => s!.Recipe != settings.Recipe)) return "Every line in a race comes from the same recipe.";

        return live.FindRecipe(settings.Recipe) is { } installed && Fits(PanelType.Race, installed.Recipe) ? null : $"These {words} have no total to race.";
    }

    private static string? ToSourceProblem(PanelSettings settings, LiveBoard live, string word)
    {
        if (settings.ToSourceId is null) return $"Choose a {word} to compare with.";
        if (live.FindSource(settings.ToSourceId) is not { } to) return $"{PanelText.StaleSource(word)} {ChooseAnother}";

        return to.Recipe != settings.Recipe || to.Id == settings.SourceId ? $"Choose a different {word} of the same recipe to compare with." : null;
    }

    private static string? StatProblem(PanelType type, PanelSettings settings, InstalledRecipe? installed)
    {
        if (settings.Stat is null) return type == PanelType.PastPeriods ? null : "Choose a stat.";
        if (installed is null || RecipeStats.Find(installed.Recipe, settings.Stat) is null) return $"{PanelText.StaleStat} {ChooseAnother}";

        return Fits(type, installed.Recipe) ? null : "This panel can't show that stat.";
    }
}
