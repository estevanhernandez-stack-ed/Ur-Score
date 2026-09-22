using System.Globalization;
using Labs626.UrScore.Book;

namespace UrScore.Tests;

/// <summary>
/// A score book of any size, written the way the app writes one: month files under a slug folder, one JSON line
/// per reading through <see cref="BookJson.Serialize"/>, finals at each period's end. Sweep D's instrument
/// (S1-F.3, S1-F.2): the performance rows are not done until there is a number before and a number after, and a
/// number needs a book of the shape and size a season produces — which need not have taken a season to record.
/// Synthetic on purpose: deterministic, the same on every machine, and no player's id or value anywhere in it.
/// <para>
/// The shape follows the shipped clan-battle recipe. Every clan source keeps three of your accounts and two
/// headlines; a battle runs <see cref="PeriodDays"/> days and a final is written at each change of battle; a
/// clans-list source keeps the top of the board by name (<see cref="GroupRows.Top"/> of them). Values climb
/// steadily so that <c>Collapse</c> has nothing to fold, which is the honest worst case for a chart.
/// </para>
/// </summary>
internal static class BookGenerator
{
    public const string ClanSlug = "pet-sim-99-clan-battle-points";

    public const string ListSlug = "pet-sim-99-top-clans";

    public const int PeriodDays = 4;

    public static readonly long[] Accounts = [101, 201, 202];

    public static readonly DateTimeOffset End = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    /// <summary>What was written, so a benchmark can say what it measured.</summary>
    public sealed record Written(int Lines, int Finals, long Bytes, IReadOnlyList<string> ClanSources, string ListSource);

    /// <param name="clanSources">How many clan sources of the battle recipe, each reading every <paramref name="everySeconds"/>.</param>
    /// <param name="days">How many days back from <see cref="End"/> the book reaches.</param>
    public static Written Write(string root, int clanSources, int days, int everySeconds = 180, int groupsKept = GroupRows.Top)
    {
        var start = End.AddDays(-days);
        var clans = Enumerable.Range(1, clanSources).Select(i => $"s-{i:x8}").ToList();
        const string list = "s-000000ff";
        var lines = 0;
        var finals = 0;
        long bytes = 0;

        // One writer per (slug, month) so the files are appended in time order, as the app appends them.
        var open = new Dictionary<string, StreamWriter>(StringComparer.Ordinal);
        StreamWriter Writer(string slug, DateTimeOffset t)
        {
            var file = BookFiles.MonthFile(root, slug, t);
            if (!open.TryGetValue(file, out var writer))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                open[file] = writer = new StreamWriter(file, append: false);
            }

            return writer;
        }

        void Put(string slug, BookLine line)
        {
            var text = BookJson.Serialize(line);
            Writer(slug, line.T).WriteLine(text);
            bytes += text.Length + 1;
            lines++;
        }

        try
        {
            var previousPeriod = -1;
            for (var t = start; t <= End; t = t.AddSeconds(everySeconds))
            {
                var period = (int)((t - start).TotalDays / PeriodDays);
                var battle = new BookPeriod($"Battle{period:D3}", start.AddDays(period * PeriodDays), start.AddDays((period + 1) * PeriodDays));
                var minutes = (t - start).TotalMinutes;

                if (period != previousPeriod && previousPeriod >= 0)
                {
                    // The battle just ended: a final per clan source, as the watch writes one from the next read.
                    var ended = new BookPeriod($"Battle{previousPeriod:D3}");
                    foreach (var (source, k) in clans.Select((s, k) => (s, k)))
                    {
                        Put(ClanSlug, Line(BookLine.KindFinal, BookLine.TriggerTimer, t, source, k, ended, minutes));
                        finals++;
                    }
                }

                previousPeriod = period;

                foreach (var (source, k) in clans.Select((s, k) => (s, k)))
                {
                    Put(ClanSlug, Line(BookLine.KindRead, BookLine.TriggerTimer, t, source, k, battle, minutes));
                }

                Put(ListSlug, ListLine(t, list, battle, minutes, groupsKept));
            }
        }
        finally
        {
            foreach (var writer in open.Values) writer.Dispose();
        }

        return new Written(lines, finals, bytes, clans, list);
    }

    private static BookLine Line(string kind, string trigger, DateTimeOffset t, string source, int clan, BookPeriod period, double minutes)
    {
        var accounts = new Dictionary<string, BookAccount>(StringComparer.Ordinal);
        foreach (var (id, i) in Accounts.Select((id, i) => (id, i)))
        {
            // Steadily climbing, distinct per clan and account, so no two consecutive readings are equal.
            var value = Math.Round((clan + 1) * 1_000_000 + (i + 1) * 100_000 + minutes * 250, 0);
            accounts[id.ToString(CultureInfo.InvariantCulture)] = new BookAccount(
                new Dictionary<string, double> { ["value"] = value }, new Dictionary<string, int> { ["value"] = i + 1 }, 48);
        }

        return new BookLine(
            BookLine.Version, kind, t, -300, trigger,
            new BookRecipeRef(ClanSlug, "0123456789abcdef"), source, "mine",
            new Dictionary<string, string> { ["clan"] = $"Clan{clan}" },
            period,
            new Dictionary<string, double> { ["clan-place"] = 10 + clan, ["clan-points"] = Math.Round((clan + 1) * 40_000_000 + minutes * 900, 0) },
            ["value"],
            accounts);
    }

    private static BookLine ListLine(DateTimeOffset t, string source, BookPeriod period, double minutes, int groupsKept)
    {
        var groups = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var g = 0; g < groupsKept; g++)
        {
            groups[$"Group{g:D2}"] = Math.Round((groupsKept - g) * 30_000_000 + minutes * (700 - g * 9), 0);
        }

        return new BookLine(
            BookLine.Version, BookLine.KindRead, t, -300, BookLine.TriggerTimer,
            new BookRecipeRef(ListSlug, "fedcba9876543210"), source, "watch",
            new Dictionary<string, string>(),
            period,
            new Dictionary<string, double> { ["field-top"] = groups.Values.Max(), ["field-mine"] = groups["Group07"] },
            [],
            new Dictionary<string, BookAccount>(StringComparer.Ordinal),
            Groups: groups);
    }
}
