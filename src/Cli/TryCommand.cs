using System.Globalization;
using System.IO;
using System.Text.Json;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Cli;

/// <summary>
/// Score book spec §10: runs a recipe once and prints what it found. No window, RoRoRo, state, book or
/// single-instance mutex. Never prints a player's id or name: only counts, the smallest, median and
/// largest value, and the accounts given with --account.
/// </summary>
public static class TryCommand
{
    public const int Ok = 0;
    public const int Refused = 2;
    public const int InputMissing = 3;
    public const int Stopped = 4;
    public const int BadArguments = 5;

    public const string Usage =
        "Usage: 626labs.ur-score.exe --try <recipe.json> [--input id=value]... [--stat key]... [--account robloxUserId]... [--json]";

    public static bool Wants(string[] args) => args.Contains("--try", StringComparer.Ordinal);

    public static async Task<int> RunAsync(string[] args, TextWriter output, IRecipeTransport transport, IKeyStore keys, CancellationToken cancellationToken)
    {
        if (!TryParseArguments(args, out var parsed, out var argumentProblem))
        {
            output.WriteLine(argumentProblem);
            output.WriteLine(Usage);
            return BadArguments;
        }

        var result = RecipeParser.Parse(await File.ReadAllTextAsync(parsed.File, cancellationToken).ConfigureAwait(false));
        if (!result.Ok)
        {
            output.WriteLine("This recipe was refused:");
            foreach (var problem in result.Problems) output.WriteLine("  " + problem);
            return Refused;
        }

        var recipe = result.Recipe!;
        foreach (var input in recipe.Inputs.Where(i => !parsed.Inputs.ContainsKey(i.Id)))
        {
            output.WriteLine($"Set {input.Label} with --input {input.Id}=<value>.");
            return InputMissing;
        }

        if (recipe.LastStep.PerAccount && parsed.Accounts.Count == 0)
        {
            output.WriteLine("This recipe reads per account. Add --account <robloxUserId> for each account to read.");
            return InputMissing;
        }

        var offered = RecipeStats.Offered(recipe, parsed.Stats).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var unknown = parsed.Stats.FirstOrDefault(s => !offered.Contains(s));
        if (unknown is not null)
        {
            output.WriteLine($"The recipe has no stat '{unknown}'.");
            output.WriteLine(Usage);
            return BadArguments;
        }

        IReadOnlySet<string> tracked = parsed.Stats.Count > 0
            ? parsed.Stats.ToHashSet(StringComparer.Ordinal)
            : recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);

        var reading = await new RecipeEngine(transport, keys)
            .ReadAsync(recipe, parsed.Inputs, parsed.Accounts, tracked, cancellationToken)
            .ConfigureAwait(false);

        var report = Report(recipe, keys, reading, tracked, parsed.Accounts);
        if (parsed.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        }
        else
        {
            WriteText(output, report);
        }

        return reading.Outcome == ReadingOutcome.Read ? Ok : Stopped;
    }

    private sealed record Arguments(string File, Dictionary<string, string> Inputs, List<string> Stats, List<long> Accounts, bool Json);

    private static bool TryParseArguments(string[] args, out Arguments parsed, out string problem)
    {
        parsed = new Arguments("", new Dictionary<string, string>(StringComparer.Ordinal), [], [], false);
        problem = "";
        string? file = null;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (args[i])
            {
                case "--try":
                    file = Next();
                    break;
                case "--input":
                    // Restructured from a single "is not {} pair || pair.IndexOf(...) is var eq and <= 0"
                    // pattern-match (CS0165: the compiler does not treat 'eq' as definitely assigned on
                    // the fall-through path when a var pattern sits inside an 'and' on the right of '||').
                    // Same condition, same message, same result.
                    if (Next() is not { } pair)
                    {
                        problem = "--input needs id=value.";
                        return false;
                    }

                    var eq = pair.IndexOf('=');
                    if (eq <= 0)
                    {
                        problem = "--input needs id=value.";
                        return false;
                    }

                    parsed.Inputs[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    break;
                case "--stat":
                    if (Next() is not { Length: > 0 } stat)
                    {
                        problem = "--stat needs a stat key.";
                        return false;
                    }

                    parsed.Stats.Add(stat);
                    break;
                case "--account":
                    if (!long.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    {
                        problem = "--account needs a Roblox user id.";
                        return false;
                    }

                    parsed.Accounts.Add(id);
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    problem = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

        if (file is null)
        {
            problem = "--try needs a recipe file.";
            return false;
        }

        if (!System.IO.File.Exists(file))
        {
            problem = $"No recipe file at {file}.";
            return false;
        }

        parsed = parsed with { File = file, Json = json };
        return true;
    }

    private sealed record StatSummary(string Key, int Found, int Missed, double? Smallest, double? Median, double? Largest);

    /// <summary>
    /// One account asked about: its values, its rank per stat, the row count (<c>Of</c>, as the book's <c>of</c>) and, per
    /// stat, how many rows that rank was counted among (<c>Ranked</c>, as the book's <c>ranked</c>; backlog S1-6.9).
    /// </summary>
    private sealed record AccountSummary(
        long UserId, IReadOnlyDictionary<string, double> Values, IReadOnlyDictionary<string, int> Rank, int? Of, IReadOnlyDictionary<string, int> Ranked);

    private sealed record TryReport(
        string Recipe, IReadOnlyList<string> Contacts, string Outcome, string? Detail, int RowsSeen, string? Period, string? PeriodEnds,
        IReadOnlyList<string> PastPeriods, IReadOnlyDictionary<string, string?> Headline, IReadOnlyList<StatSummary> Stats,
        int CounterNames, IReadOnlyList<AccountSummary> Accounts, int Groups, IReadOnlyList<string> FirstGroups);

    private static TryReport Report(Recipe recipe, IKeyStore keys, RecipeReading reading, IReadOnlySet<string> tracked, IReadOnlyList<long> accounts)
    {
        var contacts = ImportReview.Review(recipe, keys).Hosts.Select(h => $"{h.Host}: {ImportReview.SendsText(h)}").ToList();

        var stats = tracked.Order(StringComparer.Ordinal).Select(key =>
        {
            var values = reading.Rows.Where(r => r.Values.ContainsKey(key)).Select(r => r.Values[key]).Order().ToList();
            return new StatSummary(key, values.Count, reading.Rows.Count - values.Count,
                values.Count == 0 ? null : values[0], values.Count == 0 ? null : values[(values.Count - 1) / 2], values.Count == 0 ? null : values[^1]);
        }).ToList();

        var ranks = tracked.ToDictionary(key => key, key => Ranking.Competition(reading.Rows, key), StringComparer.Ordinal);
        var yours = accounts.Distinct()
            .Select(id => reading.Rows.FirstOrDefault(r => r.UserId == id) is { } row
                ? new AccountSummary(id, row.Values,
                    ranks.Where(r => r.Value.ContainsKey(id)).ToDictionary(r => r.Key, r => r.Value[id], StringComparer.Ordinal),
                    recipe.LastStep.PerAccount ? null : reading.Rows.Count,
                    ranks.Where(r => r.Value.ContainsKey(id)).ToDictionary(r => r.Key, r => r.Value.Count, StringComparer.Ordinal))
                : new AccountSummary(id, new Dictionary<string, double>(), new Dictionary<string, int>(), null, new Dictionary<string, int>()))
            .ToList();

        return new TryReport(
            recipe.Name, contacts, reading.Outcome.ToString(), reading.Detail, reading.RowsSeen,
            reading.Period?.Value, reading.Period?.Ends?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            [.. reading.Past.Select(p => p.Value)],
            reading.Headline.ToDictionary(h => h.Id.Length > 0 ? h.Id : h.Label, h => h.Text, StringComparer.Ordinal),
            stats, reading.CounterNames.Count, yours, reading.Groups.Count, [.. reading.Groups.Take(10).Select(g => g.Name)]);
    }

    private static void WriteText(TextWriter output, TryReport report)
    {
        static string N(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "none";

        output.WriteLine($"Recipe: {report.Recipe}");
        output.WriteLine("Contacts:");
        foreach (var contact in report.Contacts) output.WriteLine("  " + contact);
        output.WriteLine($"Outcome: {report.Outcome}");
        if (report.Detail is not null) output.WriteLine($"Detail: {report.Detail}");
        output.WriteLine($"Rows seen: {report.RowsSeen}");
        if (report.Period is not null) output.WriteLine($"Period: {report.Period}{(report.PeriodEnds is null ? "" : $" (ends {report.PeriodEnds})")}");
        if (report.PastPeriods.Count > 0) output.WriteLine($"Past periods: {report.PastPeriods.Count} ({string.Join(", ", report.PastPeriods)})");

        if (report.Headline.Count > 0)
        {
            output.WriteLine("Headline:");
            foreach (var (id, text) in report.Headline) output.WriteLine($"  {id} = {text ?? "none"}");
        }

        output.WriteLine("Stats:");
        foreach (var stat in report.Stats)
        {
            output.WriteLine($"  {stat.Key}: found in {stat.Found}, missed in {stat.Missed}, smallest {N(stat.Smallest)}, median {N(stat.Median)}, largest {N(stat.Largest)}");
        }

        if (report.CounterNames > 0) output.WriteLine($"Counter names: {report.CounterNames}");

        foreach (var account in report.Accounts)
        {
            var values = account.Values.Count == 0 ? "not in the results" : string.Join(", ", account.Values.Select(v => $"{v.Key}={N(v.Value)}"));
            // Each rank of its own field: "rank 1 of 2" counts the rows that had that stat, not every row read (backlog S1-6.9).
            var rank = account.Rank.Count == 0 || account.Of is null ? "" : $", rank {string.Join(", ", account.Rank.Select(r => $"{r.Value} of {account.Ranked[r.Key]}"))}";
            output.WriteLine($"  {account.UserId}: {values}{rank}");
        }

        if (report.Groups > 0) output.WriteLine($"Groups: {report.Groups} ({string.Join(", ", report.FirstGroups)})");
    }
}
