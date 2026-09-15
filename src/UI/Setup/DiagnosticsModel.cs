using System.Text;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record SourceDiagnostic(string SourceId, string Name, string State, string Detail, string LastRead, string NextRead, string Misses)
{
    public string Timing => $"Last read {LastRead} · next read {NextRead}";

    public bool HasDetail => Detail.Length > 0;

    public bool HasMisses => Misses.Length > 0;
}

/// <summary>Setup › Diagnostics (spec §7.7). Copy diagnostics carries counts about the book, never its lines.</summary>
public static class DiagnosticsModel
{
    public const int TrailLines = 40;

    public static string StateText(WatchState state) => state switch
    {
        WatchState.NeedsInput => "Waiting for a value to be set.",
        WatchState.SourceUnreachable => "Could not reach the data.",
        WatchState.InputNotFound => "Nothing matched what was entered.",
        WatchState.SourceIdle => "Nothing to read right now.",
        WatchState.ShapeNotUnderstood => "The response was not a shape Ur Score understands.",
        WatchState.NoMatches => "None of your accounts are in what came back.",
        WatchState.Reporting => "Reporting to RoRoRo.",
        WatchState.HostDown => "RoRoRo is not running.",
        WatchState.Rejected => "RoRoRo refused the report.",
        WatchState.RateLimited => "The source asked Ur Score to slow down.",
        WatchState.SignInRequired => "The source wants signing in, which recipes cannot do.",
        WatchState.KeyMissing => "A key is needed.",
        WatchState.KeyRejected => "The source rejected the key.",
        WatchState.Showing => "Reading. No stat is set to send to RoRoRo.",
        _ => state.ToString(),
    };

    public static IReadOnlyList<SourceDiagnostic> Sources(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> latest,
        Func<string, DateTimeOffset?> lastRead, bool running, IReadOnlyList<HostAccount> accounts, DateTimeOffset now, Redactor redactor)
    {
        var rows = new List<SourceDiagnostic>();

        foreach (var source in sources)
        {
            var recipe = installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe;
            var snapshot = latest.GetValueOrDefault(source.Id);
            var last = lastRead(source.Id);

            var state = !source.Enabled ? "Switched off."
                : snapshot is null ? (running ? "Waiting for its first read." : "Not started.")
                : StateText(snapshot.State);

            string next;
            if (!source.Enabled || recipe is null) next = StatText.Dash;
            else if (!running) next = "when you press Start";
            else if (last is null) next = "soon";
            else
            {
                var due = last.Value.AddSeconds(recipe.EffectiveEverySeconds);
                next = due <= now ? "due now" : $"in {StatText.Span(due - now)}";
            }

            rows.Add(new SourceDiagnostic(
                source.Id,
                recipe is null ? $"{source.Recipe} (not installed)" : ScoreBookModel.SourceLabel(recipe, source),
                state,
                redactor.Redact(snapshot?.Detail),
                last is null ? "never" : $"{StatText.Span(now - last.Value)} ago",
                next,
                redactor.Redact(Misses(recipe, snapshot, accounts))));
        }

        return rows;
    }

    /// <summary>Stat-wide misses, then each of your accounts' cell misses by display name. Another row's id is never named.</summary>
    public static string Misses(Recipe? recipe, RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot is null) return "";

        var parts = new List<string>();
        foreach (var (key, miss) in snapshot.StatMisses)
        {
            parts.Add($"{Label(recipe, key)}: {miss}");
        }

        foreach (var ((userId, stat), reason) in snapshot.CellMisses)
        {
            if (accounts.FirstOrDefault(a => a.RobloxUserId == userId && userId != 0)?.DisplayName is not { } name) continue;
            parts.Add($"{name} · {Label(recipe, stat)}: {reason}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    public static string CopyText(
        DateTimeOffset now, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyList<SourceDiagnostic> rows,
        bool resolveNames, string hostText, string bookRoot, int bookPending, int bookDropped,
        IReadOnlyList<string> trail, Redactor redactor)
    {
        // global:: because this namespace's Source alias hides the Labs626.UrScore.Source namespace; outside the
        // interpolation, since a colon inside a hole starts a format.
        var userAgent = global::Labs626.UrScore.Source.UrScoreIdentity.UserAgent;
        var text = new StringBuilder()
            .AppendLine($"Ur Score diagnostics {now:O}")
            .AppendLine($"user-agent={userAgent} resolveNames={resolveNames}")
            .AppendLine(hostText)
            .AppendLine($"score book in {bookRoot}: pending={bookPending} dropped={bookDropped} (no book content is included)");

        foreach (var item in installed)
        {
            var sent = string.Join(", ", item.State.SentStats(item.Recipe).Select(s => $"{s.Key}->{s.MetricId}"));
            var shown = string.Join(", ", item.State.ShownStats(item.Recipe).Select(s => s.Key));
            text.AppendLine($"recipe={item.Recipe.Slug} poll={item.Recipe.EffectiveEverySeconds}s groupList={item.Recipe.IsGroupList} "
                            + $"sent={(sent.Length == 0 ? "(none)" : sent)} shown={(shown.Length == 0 ? "(none)" : shown)}");
        }

        foreach (var source in sources)
        {
            var inputs = source.Inputs.Count == 0 ? "(none)" : string.Join(", ", source.Inputs.Select(kv => $"{kv.Key}={kv.Value}"));
            text.AppendLine($"source={source.Id} recipe={source.Recipe} role={source.Role} enabled={source.Enabled} inputs={inputs}");

            if (rows.FirstOrDefault(r => r.SourceId == source.Id) is not { } row) continue;
            text.AppendLine($"  state={row.State} last={row.LastRead} next={row.NextRead}");
            if (row.HasDetail) text.AppendLine($"  detail={row.Detail}");
            if (row.HasMisses) text.AppendLine($"  misses={row.Misses.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}");
        }

        text.AppendLine().AppendLine(string.Join(Environment.NewLine, trail.TakeLast(TrailLines)));

        // Redacted as a whole, last, so nothing added above can carry a key out.
        return redactor.Redact(text.ToString());
    }

    private static string Label(Recipe? recipe, string key) =>
        recipe is not null && RecipeStats.Find(recipe, key) is { } stat ? stat.Label : key;
}
