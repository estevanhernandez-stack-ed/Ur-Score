using System.Text.Json;

namespace Labs626.UrScore.Source;

/// <summary>One contributor's cumulative points for the live battle.</summary>
public sealed record Contribution(long UserId, double Points);

/// <summary>
/// Which battle is live. <see cref="ConfigName"/> null with <see cref="Miss"/> null means no battle
/// is running, which is the normal state most of the time and must never read as an error.
/// <para>
/// <see cref="MissIsTransport"/> separates "we could not reach it" from "we could not read it".
/// The watch loop shows those as different states because the remedies are nothing alike: one is
/// wait, the other is someone needs to look at the shape.
/// </para>
/// </summary>
public sealed record BattleProbe(string? ConfigName, string? Miss, bool MissIsTransport = false);

/// <summary>
/// The contributions, or an explanation. An empty list with a null <see cref="Miss"/> is a real
/// empty list — a battle nobody has scored in — and is deliberately distinguishable from a shape
/// that could not be read.
/// </summary>
public sealed record ContributionsResult(
    IReadOnlyList<Contribution> Contributions, string? Miss, bool MissIsTransport = false);

/// <summary>
/// Extraction that survives being wrong about the shape.
/// <para>
/// Every miss names the keys that WERE present at the level it failed, because the alternative —
/// an empty list — is indistinguishable from a quiet clan, and because the vendor's own
/// documentation is stale enough that the next person to debug this will not be able to trust it.
/// </para>
/// </summary>
public static class ClanParser
{
    public static BattleProbe ActiveBattle(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var body = JsonNav.Unwrap(document.RootElement);

            // A null or absent body is "no battle running". Real, normal, and not a miss.
            if (body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new BattleProbe(null, null);
            }

            if (!JsonNav.TryGet(body, "configName", out var configName)
                || configName.ValueKind != JsonValueKind.String)
            {
                // No configName anywhere. Could be "no battle" in a shape we have not seen, so say
                // what was there and let a human decide which.
                var keys = JsonNav.Keys(body);
                return new BattleProbe(null, keys.Count == 0
                    ? "The active-battle response held no object to read."
                    : $"No 'configName' in the active-battle response. Keys present: {string.Join(", ", keys)}.");
            }

            var name = configName.GetString();
            return string.IsNullOrWhiteSpace(name)
                ? new BattleProbe(null, null)   // present but empty: no battle running
                : new BattleProbe(name, null);
        }
        catch (JsonException ex)
        {
            return new BattleProbe(null, $"The active-battle response was not valid JSON: {ex.Message}");
        }
    }

    public static ContributionsResult Contributions(string json, string configName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var body = JsonNav.Unwrap(document.RootElement);

            if (!JsonNav.TryGet(body, "Battles", out var battles)
                || battles.ValueKind != JsonValueKind.Object)
            {
                return Miss($"No 'Battles' object in the clan response. Keys present: "
                            + $"{Join(JsonNav.Keys(body))}.");
            }

            if (!JsonNav.TryGet(battles, configName, out var battle)
                || battle.ValueKind != JsonValueKind.Object)
            {
                return Miss($"The clan response has no battle named '{configName}'. "
                            + $"Battles present: {Join(JsonNav.Keys(battles))}.");
            }

            if (!JsonNav.TryGet(battle, "PointContributions", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                return Miss($"Battle '{configName}' has no 'PointContributions' array. "
                            + $"Keys present: {Join(JsonNav.Keys(battle))}.");
            }

            var contributions = new List<Contribution>();
            foreach (var row in rows.EnumerateArray())
            {
                // One unreadable row costs only itself. A response that grew a summary object in
                // the middle of the array must not discard the rows around it.
                if (!JsonNav.TryGet(row, "UserID", out var userId)) continue;
                if (!JsonNav.TryGet(row, "Points", out var points)) continue;
                if (!JsonNav.TryNumber(userId, out var id)) continue;
                if (!JsonNav.TryNumber(points, out var value)) continue;

                contributions.Add(new Contribution((long)id, value));
            }

            return new ContributionsResult(contributions, null);
        }
        catch (JsonException ex)
        {
            return Miss($"The clan response was not valid JSON: {ex.Message}");
        }

        static ContributionsResult Miss(string why) => new([], why);
        static string Join(IReadOnlyList<string> keys) => keys.Count == 0 ? "(none)" : string.Join(", ", keys);
    }
}
