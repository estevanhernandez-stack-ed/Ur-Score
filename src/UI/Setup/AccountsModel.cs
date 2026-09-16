using System.ComponentModel;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One account's Send for one recipe. Raises a change so the page can check the budget and save.</summary>
public sealed class SendTick : INotifyPropertyChanged
{
    private bool _on;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string RecipeSlug { get; init; }

    public required Guid AccountId { get; init; }

    /// <summary>The accessible name: "Send &lt;account&gt; for &lt;recipe&gt;".</summary>
    public required string Name { get; init; }

    public bool On
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(On)));
        }
    }
}

public sealed record AccountRow(
    Guid AccountId, string DisplayName, string FoundIn, IReadOnlyList<SendTick> Sends, string? Avatar = null, string Note = "")
{
    /// <summary>Why a source can't read this account, in that recipe's own words (plan A44).</summary>
    public bool HasNote => Note.Length > 0;
}

/// <summary>A recipe's state after a Send change, or the refusal that undid it.</summary>
public sealed record SendChange(RecipeState State, string? Refusal);

/// <summary>Setup › Your accounts (spec §7.2).</summary>
public static class AccountsModel
{
    /// <summary>Group lists never send, so they get no Send column.</summary>
    public static IReadOnlyList<InstalledRecipe> SendingRecipes(IReadOnlyList<InstalledRecipe> installed) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList)];

    public static IReadOnlyList<AccountRow> Rows(
        IReadOnlyList<HostAccount> accounts, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest, Func<long, string?>? avatar = null)
    {
        var sending = SendingRecipes(installed);
        return [.. accounts.Select(account => new AccountRow(
            account.AccountId,
            account.DisplayName,
            FoundIn(account, installed, sources, latest),
            [.. sending.Select(r => new SendTick
            {
                RecipeSlug = r.Recipe.Slug,
                AccountId = account.AccountId,
                Name = $"Send {account.DisplayName} for {r.Recipe.Name}",
                On = !r.State.Excluded.Contains(account.AccountId),
            })],
            avatar?.Invoke(account.RobloxUserId),
            PanelText.CannotRead(account.RobloxUserId, installed, sources, latest, nameTheRecipe: true)))];
    }

    /// <summary>
    /// The main and mine sources whose last read had this account, main first. Else where it is, in the words My accounts heads
    /// the same accounts with (PanelText.NotFound): only in groups you watch, or how much has been read (backlog S1-12.7).
    /// <paramref name="latest"/> holds this session's readings only, so "not in" waits for every source to be read.
    /// </summary>
    public static string FoundIn(
        HostAccount account, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest)
    {
        var withInputs = installed.Where(SetupPages.HasClansPage).ToList();
        if (withInputs.Count == 0) return "";
        if (account.RobloxUserId == 0) return PanelText.NotMatched;

        var ofRecipes = sources
            .Where(s => s.Enabled && withInputs.Any(i => string.Equals(i.Recipe.Slug, s.Recipe, StringComparison.Ordinal)))
            .ToList();
        bool Holds(Source source) => latest.GetValueOrDefault(source.Id)?.Rows is { } rows && rows.Any(r => r.UserId == account.RobloxUserId);

        var names = new List<string>();
        foreach (var source in ofRecipes.Where(s => s.Role != SourceRole.Watch).OrderBy(s => s.Role == SourceRole.Main ? 0 : 1))
        {
            if (!Holds(source)) continue;

            var recipe = withInputs.First(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal)).Recipe;
            var name = ClansModel.NameOf(recipe, source);
            names.Add(source.Role == SourceRole.Main ? $"★ {name}" : name);
        }

        if (names.Count > 0) return string.Join(", ", names.Distinct(StringComparer.Ordinal));

        var (group, groups) = Words(withInputs);
        if (ofRecipes.Any(s => s.Role == SourceRole.Watch && Holds(s))) return PanelText.OnlyWatched(groups);

        var read = ofRecipes.Count(s => latest.GetValueOrDefault(s.Id)?.Rows is not null);
        return PanelText.NotFound($"Not in a watched {group}", groups, inHand: read, readNow: read, sources: ofRecipes.Count);
    }

    /// <summary>
    /// The recipes' own words for a group and several ("clan", "clans") when every recipe with a clans page uses the same ones,
    /// else Ur Score's own "source", "sources". The line is about all of them, so one recipe's noun never speaks for the rest
    /// (backlog S1-12.7, where it came from the first recipe alone).
    /// </summary>
    private static (string Group, string Groups) Words(IReadOnlyList<InstalledRecipe> withInputs)
    {
        var words = withInputs.Select(i => (Group: RecipeWords.Group(i.Recipe), Groups: RecipeWords.GroupsLower(i.Recipe))).Distinct().ToList();
        return words.Count == 1 ? words[0] : ("source", "sources");
    }

    /// <summary>
    /// Stats design §5.3, unchanged: a Send tick that would pass RoRoRo's history limit is refused. Entries
    /// for accounts RoRoRo isn't listing right now are kept as they are.
    /// </summary>
    public static SendChange ToggleSend(
        InstalledRecipe recipe, IReadOnlyList<InstalledRecipe> installed, IReadOnlyCollection<Guid> accountIds, Guid accountId, bool on)
    {
        var excluded = recipe.State.Excluded.ToHashSet();
        var sentStats = recipe.State.SentStats(recipe.Recipe).Count;
        var before = accountIds.Count(id => !excluded.Contains(id));

        if (on) excluded.Remove(accountId);
        else excluded.Add(accountId);

        var after = accountIds.Count(id => !excluded.Contains(id));

        if (on)
        {
            var budget = HistoryBudget.Check(
                HistoryBudget.Installed(installed, accountIds, exceptSlug: recipe.Recipe.Slug),
                (before, sentStats), (after, sentStats), accountsKnown: true);
            if (!budget.Allowed) return new SendChange(recipe.State, budget.Line);
        }

        var kept = (recipe.State.ExcludedAccountIds ?? [])
            .Where(id => !Guid.TryParse(id, out var parsed) || parsed != accountId)
            .ToList();
        if (!on) kept.Add(accountId.ToString());

        return new SendChange(recipe.State with { ExcludedAccountIds = kept }, null);
    }

    /// <summary>
    /// The page's asking for your accounts threw. The ask itself never fails for RoRoRo's sake (a slow or broken answer gives the
    /// saved list), so what threw is what Ur Score did with the answer: the words say only what is known (backlog S1-12.4).
    /// </summary>
    public const string AccountsNotUpdated = "Something went wrong while Ur Score was getting your accounts. Diagnostics has the details.";

    /// <summary>
    /// The page's message line, worked out on every redraw so a redraw can't wipe what it said (backlog S1-12.4): why the last
    /// Send tick was undone, the newest thing you did; else what went wrong getting your accounts; else the budget warning.
    /// </summary>
    public static string MessageLine(string? refusal, string? problem, string? budgetWarning) => refusal ?? problem ?? budgetWarning ?? "";

    public static string ListedLine(AccountList? last, DateTimeOffset? savedAt, DateTimeOffset now)
    {
        const string Lead = "Accounts come from RoRoRo.";

        if (last is { Denied: true })
        {
            return $"{Lead} RoRoRo refused to list them: host.queries.accounts is not granted. "
                   + "Remove Ur Score from RoRoRo's Plugins page and reinstall it to be asked again.";
        }

        var listed = last?.ListedAt ?? savedAt;
        if (listed is null) return $"{Lead} RoRoRo hasn't listed them yet.";

        return last is { FromCache: true }
            ? $"{Lead} RoRoRo isn't answering, so these are the ones it listed {Ago(listed.Value, now)}."
            : $"{Lead} Last listed {Ago(listed.Value, now)}.";
    }

    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromHours(48)) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} days ago";
    }
}
