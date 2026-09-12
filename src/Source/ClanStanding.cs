using System.Text.Json;

namespace Labs626.UrScore.Source;

/// <summary>One row of the leaderboard, as the window renders it.</summary>
public sealed record RankedContribution(int Position, long UserId, double Points, bool IsMine);

/// <summary>
/// Where the clan sits, and the half of the clan response this plugin used to discard.
/// <para>
/// Both values are nullable: a battle can be live before either is populated, and a missing
/// standing is an absence rather than a failure. `Place` and `Points` sit beside
/// `PointContributions` in the same object the parser already walks — `Place` had appeared exactly
/// once in this repo's documents, as filler in a test demonstrating a key that gets stepped past.
/// Reading it costs one lookup and is the difference between a diagnostic panel and something worth
/// opening when no alert is configured (spec §6.1).
/// </para>
/// <para>
/// Deliberately quiet about failure, unlike <see cref="ClanParser"/>. The parser owns reporting a
/// shape it could not read and does it well; standing runs alongside on the same response, so a
/// second voice complaining about the same bytes would be noise. Missing values are null.
/// </para>
/// </summary>
public sealed record ClanStanding(long? Place, double? Points)
{
    /// <summary>Reads the standing out of the clan response. Null values on anything unreadable.</summary>
    public static ClanStanding Read(string json, string configName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var body = JsonNav.Unwrap(document.RootElement);

            if (!JsonNav.TryGet(body, "Battles", out var battles)) return Empty;
            if (!JsonNav.TryGet(battles, configName, out var battle)) return Empty;

            long? place = JsonNav.TryGet(battle, "Place", out var placeElement)
                          && JsonNav.TryNumber(placeElement, out var placeValue)
                ? (long)placeValue
                : null;

            double? points = JsonNav.TryGet(battle, "Points", out var pointsElement)
                             && JsonNav.TryNumber(pointsElement, out var pointsValue)
                ? pointsValue
                : null;

            return new ClanStanding(place, points);
        }
        catch (JsonException)
        {
            // The parser is already reporting this response's shape problem properly. A second
            // complaint about the same bytes would be noise, so standing simply has nothing.
            return Empty;
        }
    }

    private static ClanStanding Empty { get; } = new(null, null);

    /// <summary>
    /// The leaderboard, highest first, with the user's own accounts marked.
    /// <para>
    /// Positions are distinct even on tied points: sharing a number makes the list read as though
    /// a row were missing. Ties break on user id so the order is stable across polls — an
    /// unstable sort would reshuffle the leaderboard every three minutes for no visible reason.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RankedContribution> Rank(
        IReadOnlyList<Contribution> contributions, IReadOnlySet<long> mine)
    {
        return [.. contributions
            .OrderByDescending(c => c.Points)
            .ThenBy(c => c.UserId)
            .Select((c, index) => new RankedContribution(index + 1, c.UserId, c.Points, mine.Contains(c.UserId)))];
    }
}
