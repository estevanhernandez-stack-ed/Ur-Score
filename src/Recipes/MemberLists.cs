using System.Text.Json;
using Labs626.UrScore.Fetch;

namespace Labs626.UrScore.Recipes;

/// <summary>Which of your own Roblox ids a members list holds, or why it couldn't be read. Never anyone else's id.</summary>
public sealed record MembersResult(IReadOnlySet<long> Found, string? Problem);

/// <summary>
/// The one path that reads an input's <c>members</c> list (name your clan once, 0.7.0; backlog V3-S.20). Through the shared
/// recipe transport, so the request is spaced per host and carries the usual User-Agent. A members list holds every member's
/// Roblox id, and other members are compared against <paramref name="yours"/> and dropped inside the read: the result carries
/// the intersection only, and nothing about the list is kept, cached, logged or written (README "What leaves your machine").
/// </summary>
public sealed class MemberLists(IRecipeTransport transport)
{
    /// <summary>The transport label. The app's transport keeps no raw response, so no body with strangers' ids reaches disk.</summary>
    public const string Label = "members";

    /// <param name="inputs">The input values that fill the members url: the clan you picked.</param>
    /// <param name="yours">Your accounts' Roblox ids. With none, nothing is asked: there would be nothing to find.</param>
    public async Task<MembersResult> FindAsync(
        RecipeMembers members, IReadOnlyDictionary<string, string> inputs, IReadOnlySet<long> yours, CancellationToken cancellationToken)
    {
        if (yours.Count == 0) return new MembersResult(new HashSet<long>(), null);

        var host = RecipeHosts.HostOf(members.Url);
        if (!Uri.TryCreate(Placeholders.Fill(members.Url, inputs, encode: true), UriKind.Absolute, out var address))
        {
            return Failed($"The members address for {host} is not valid.");
        }

        var fetched = await transport.GetAsync(address, new Dictionary<string, string>(), Label, cancellationToken).ConfigureAwait(false);
        if (!fetched.Answered) return Failed(fetched.Error ?? $"Could not reach {host}.");
        if (!fetched.Succeeded) return Failed($"{host} answered {fetched.Status}.");

        try
        {
            using var document = JsonDocument.Parse(fetched.Body ?? "");
            var list = RecipePath.Resolve(document.RootElement, members.List);
            if (list.Outcome != PathOutcome.Found || list.Value.ValueKind != JsonValueKind.Array)
            {
                return Failed($"{host} did not return a list at '{members.List}'.");
            }

            // Compared one at a time and dropped: only a match is ever added, so no other member's id outlives this loop.
            var found = new HashSet<long>();
            foreach (var item in list.Value.EnumerateArray())
            {
                if (Yours(RecipePath.Resolve(item, members.UserId, "this member"), yours, out var id)) found.Add(id);
            }

            // The owner is not in the list (probed live), and an account of yours that owns the clan is in it all the same.
            if (members.Owner is { } owner && Yours(RecipePath.Resolve(document.RootElement, owner), yours, out var ownerId)) found.Add(ownerId);

            return new MembersResult(found, null);
        }
        catch (JsonException)
        {
            return Failed($"{host} did not return valid JSON.");
        }
    }

    private static bool Yours(PathResult result, IReadOnlySet<long> yours, out long id)
    {
        id = 0;
        return result.Outcome == PathOutcome.Found && JsonNav.TryUserId(result.Value, out id) && yours.Contains(id);
    }

    private static MembersResult Failed(string problem) => new(new HashSet<long>(), problem);
}
