using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>How fast this clan is going, how fast the field is, and what catching the place above would take.</summary>
public sealed record PaceModel(PanelHead Head, IReadOnlyList<FactModel> Facts);

/// <summary>
/// The Pace panel's own words and numbers. Kept apart from <see cref="PanelModels"/> because every line here has a
/// rule about honesty attached to it: a pace states its window, a window too short says so instead of projecting,
/// and a chase that cannot be won says that rather than a number nobody can reach.
/// </summary>
public static class PaceText
{
    public const string TooEarly = "too early to say";

    /// <summary>A rate as the board says numbers: "184.6M/h".</summary>
    public static string PerHour(double value) => $"{PanelText.Short(value)}/h";

    /// <summary>A pace with the window it came from, so nobody reads a five-minute burst as a day's rate.</summary>
    public static string Window(PaceWindow? window, DateTimeOffset now) =>
        window is null ? TooEarly : $"{PerHour(window.PerHour)} over {StatText.Span(window.Span)}";

    /// <summary>A pace with when its window opened, for the lines that measure from a moment rather than a length.</summary>
    public static string Since(PaceWindow? window, TimeZoneInfo zone) =>
        window is null
            ? TooEarly
            : $"{PerHour(window.PerHour)} since {TimeZoneInfo.ConvertTime(window.From, zone).ToString("ddd HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>Where the current pace lands by the end, or nothing to say when there is no pace or no end.</summary>
    public static string OnThisPace(PaceWindow? pace, double latest, DateTimeOffset? ends, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (pace is null) return TooEarly;
        if (ends is not { } end || end <= now) return "—";

        var at = latest + (pace.PerHour * (end - now).TotalHours);
        return $"{PanelText.Short(at)} by {TimeZoneInfo.ConvertTime(end, zone).ToString("ddd HH:mm", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The chase, in the words its verdict earns. "Out of reach" is measured against this clan's own best hour of
    /// this battle, never a feeling, and the certain one against what all the time left could add at that best hour.
    /// </summary>
    public static string Chase(PaceChase chase, double mine) => chase.Verdict switch
    {
        PaceVerdict.Ended => "the battle has ended",
        PaceVerdict.OnTrack when chase.CatchIn is { } When => $"passed in {StatText.Span(When)} at these paces",
        PaceVerdict.OnTrack => "you are ahead of them",
        PaceVerdict.NeedsALift => $"needs {PerHour(chase.Needed ?? 0)}, you are at {PerHour(mine)}",
        PaceVerdict.OutOfReach => $"out of reach: needs {PerHour(chase.Needed ?? 0)}, above your best hour",
        _ => "out of reach even if they stop now",
    };
}

/// <summary>
/// Builds the Pace panel: this clan's own pace from the score book, the field's from the clans list, and the chase
/// for the place above. Every line is left out rather than guessed when the book cannot answer it yet.
/// </summary>
public static class PacePanel
{
    public static PaceModel Of(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.Pace, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed || live.FindSource(settings.SourceId) is not { } source)
        {
            return new PaceModel(PanelModels.StaleSource(live, settings, title), []);
        }

        var recipe = installed.Recipe;
        var name = live.SourceName(source);
        var head = new PanelHead(
            PanelText.Title(PanelType.Pace, recipe, live.Installed), name, live.ChipRole(source), live.IsOverdue(source));

        var totalId = recipe.Headline.FirstOrDefault(h => h.Sum)?.Id;
        if (totalId is null) return new PaceModel(head with { Note = PanelText.NoTotalToRace }, []);

        var snapshot = live.SnapshotOf(source.Id);
        var period = snapshot?.Period;
        var series = reader.HeadlineSeries(source.Id, totalId, period?.Value);
        var now = live.Now;
        var zone = live.Time.LocalTimeZone;

        var hour = Pace.Over(series, now - TimeSpan.FromHours(1));
        var average = Pace.Over(series, DateTimeOffset.MinValue);
        var best = Pace.BestHour(series);
        var latest = series.Count > 0 ? series[^1].Value : 0;

        // "Now" read as though it might be the owner's own pace; it never was. Every line above the divide is this
        // clan's, and the owner's own accounts have their own lines below it (2026-09-20).
        var facts = new List<FactModel>
        {
            new("Current", PaceText.Window(hour, now)),
            new("Average", PaceText.Since(average, zone)),
            new("Best hour", best is null ? PaceText.TooEarly : PaceText.Since(best, zone)),
            new("On this pace", PaceText.OnThisPace(hour, latest, period?.Ends, now, zone)),
        };

        facts.AddRange(Yours(live, reader, installed, source, period?.Value, latest, now));
        facts.AddRange(Field(live, reader, now, period?.Value));
        if (Chase(live, reader, hour, best, period, now) is { } chase) facts.Add(chase);

        return new PaceModel(head with { Remembered = live.IsRemembered(source.Id) }, facts);
    }

    /// <summary>
    /// Your own accounts in this clan: how fast they are going together, and how much of the clan's points they are.
    /// Their readings are in the book already — the clan's pace never was theirs, however it was labelled.
    /// </summary>
    private static IEnumerable<FactModel> Yours(
        LiveBoard live, ScoreBookReader reader, InstalledRecipe installed, Source source, string? period, double clanTotal,
        DateTimeOffset now)
    {
        if (installed.State.ShownStats(installed.Recipe).FirstOrDefault() is not { } stat) yield break;

        double perHour = 0, total = 0;
        var counted = 0;
        foreach (var userId in live.MyUserIds)
        {
            var series = reader.Series(source.Id, userId, stat.Key, period, DateTimeOffset.MinValue);
            if (series.Count == 0) continue;

            total += series[^1].Value;
            counted++;
            if (Pace.Over(series, now - TimeSpan.FromHours(1)) is { } pace) perHour += pace.PerHour;
        }

        if (counted == 0) yield break;

        var label = counted == 1 ? "Your account" : $"Your {counted} accounts";
        yield return new FactModel(label, perHour > 0 ? PaceText.PerHour(perHour) : PaceText.TooEarly);
        if (clanTotal > 0) yield return new FactModel("Your share", $"{PanelText.Short(total)} · {total / clanTotal * 100:0.0}% of the clan");
    }

    /// <summary>
    /// The field's own pace, from whichever clans list is switched on. Its numbers are the field's shape, never a
    /// clan's name (<see cref="FieldSummary"/>), so these lines name positions: the leader, the top ten, the field.
    /// </summary>
    private static IEnumerable<FactModel> Field(LiveBoard live, ScoreBookReader reader, DateTimeOffset now, string? period)
    {
        if (FieldSource(live) is not { } field) yield break;

        foreach (var (label, key) in new[]
                 {
                     ("The leader", FieldSummary.Leader),
                     ("Top 10", FieldSummary.Top10),
                     ("The field", FieldSummary.Average),
                 })
        {
            var pace = Pace.Over(reader.HeadlineSeries(field.Id, key, period), DateTimeOffset.MinValue);
            if (pace is not null) yield return new FactModel(label, PaceText.PerHour(pace.PerHour));
        }
    }

    /// <summary>
    /// The chase for the place above, from the same clans list: its gap now, its pace, and what that asks of this
    /// clan. Nothing at all while the list has not been read twice — a chase needs two instants.
    /// </summary>
    private static FactModel? Chase(
        LiveBoard live, ScoreBookReader reader, PaceWindow? mine, PaceWindow? best, ReadingPeriod? period, DateTimeOffset now)
    {
        if (mine is null || FieldSource(live) is not { } field) return null;

        var gaps = reader.HeadlineSeries(field.Id, FieldSummary.GapAbove, period?.Value);
        var above = Pace.Over(reader.HeadlineSeries(field.Id, FieldSummary.Above, period?.Value), DateTimeOffset.MinValue);
        if (gaps.Count == 0 || above is null) return null;

        var places = reader.HeadlineSeries(field.Id, FieldSummary.MineRank, period?.Value);
        var place = places.Count > 0 ? (int)places[^1].Value - 1 : 0;
        var label = place > 0 ? $"To pass {PanelText.Ordinal(place)}" : "To pass the place above";

        var left = period?.Ends is { } ends && ends > now ? ends - now : TimeSpan.Zero;
        var chase = Pace.Chase(gaps[^1].Value, mine.PerHour, above.PerHour, left, best?.PerHour);
        return new FactModel(label, PaceText.Chase(chase, mine.PerHour));
    }

    /// <summary>The switched-on clans list, whose readings carry the field. None, and the panel is this clan alone.</summary>
    private static Source? FieldSource(LiveBoard live) =>
        live.Sources.FirstOrDefault(s => s.Enabled && live.FindRecipe(s.Recipe) is { Recipe.IsGroupList: true });
}
