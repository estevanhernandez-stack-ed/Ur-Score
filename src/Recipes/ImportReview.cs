namespace Labs626.UrScore.Recipes;

/// <summary>One host a recipe contacts, and exactly what goes to it (spec §6.2).</summary>
public sealed record HostContact(string Host, IReadOnlyList<string> Sends);

public sealed record ImportReviewResult(
    IReadOnlyList<HostContact> Hosts, IReadOnlyList<string> Refusals, IReadOnlyList<string> ReusedKeys)
{
    public bool CanImport => Refusals.Count == 0;
}

/// <summary>
/// <see cref="AsksAgain"/> is true for a first import, and for an update that changes a host or
/// anything sent (spec §6.3). Any other difference is listed in <see cref="Changes"/> without asking.
/// </summary>
public sealed record UpdateComparison(bool IsUpdate, bool AsksAgain, IReadOnlyList<string> Changes);

/// <summary>
/// What a recipe would do on this PC, worked out before anything runs. Pure: no network, no disk
/// beyond the key lookup, so the safety screen is testable.
/// </summary>
public static class ImportReview
{
    public const string SendsUserIds = "your accounts' Roblox user ids";

    public const string SendsNothing = "nothing about you";

    public static ImportReviewResult Review(Recipe recipe, IKeyStore keys)
    {
        var sends = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string host, string what)
        {
            if (!sends.TryGetValue(host, out var list)) sends[host] = list = [];
            if (!list.Contains(what)) list.Add(what);
        }

        // A search list is fetched with a fixed address (the parser guarantees it), so it carries
        // nothing of yours.
        foreach (var input in recipe.Inputs.Where(i => i.Search is not null))
        {
            Add(RecipeHosts.HostOf(input.Search!.Url), SendsNothing);
        }

        var refusals = new List<string>();
        var reused = new List<string>();
        var reusedLabels = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in recipe.Steps)
        {
            var host = RecipeHosts.HostOf(step.Url);
            var names = Placeholders.Names(step.Url);

            if (names.Contains(Placeholders.UserId)) Add(host, SendsUserIds);

            foreach (var input in recipe.Inputs.Where(i => names.Contains(i.Id)))
            {
                Add(host, $"the value you enter for {input.Label}");
            }

            foreach (var keyId in step.UseKeys)
            {
                var declared = recipe.Keys.First(k => k.Id == keyId);
                Add(host, $"your {declared.Label} key");

                var saved = keys.Find(keyId);
                if (saved is null) continue;

                if (!string.Equals(saved.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    refusals.Add(
                        $"This recipe would send your saved '{keyId}' key to {host}, but that key is saved for {saved.Host}. "
                        + "It was not imported. If this really is a different key, remove the saved one first.");
                }
                else if (reusedLabels.Add(declared.Label))
                {
                    reused.Add($"Uses your saved {declared.Label} key for {host}.");
                }
            }

            if (!sends.ContainsKey(host)) Add(host, SendsNothing);
        }

        // A host that receives something about you is not also "nothing about you".
        foreach (var list in sends.Values.Where(l => l.Count > 1))
        {
            list.Remove(SendsNothing);
        }

        return new ImportReviewResult([.. sends.Select(kv => new HostContact(kv.Key, kv.Value))], refusals, reused);
    }

    public static UpdateComparison CompareToInstalled(Recipe? installed, Recipe incoming, IKeyStore keys)
    {
        if (installed is null) return new UpdateComparison(false, true, []);

        var before = Flatten(Review(installed, keys));
        var after = Flatten(Review(incoming, keys));

        var added = after.Except(before).ToList();
        var removed = before.Except(after).ToList();

        var changes = new List<string>();
        changes.AddRange(added.Select(x => $"New: {x}"));
        changes.AddRange(removed.Select(x => $"No longer: {x}"));

        if (installed.EffectiveEverySeconds != incoming.EffectiveEverySeconds)
        {
            changes.Add($"Polls every {incoming.EffectiveEverySeconds}s instead of {installed.EffectiveEverySeconds}s.");
        }

        var installedMetricId = installed.LastStep.Values[0].MetricId;
        var incomingMetricId = incoming.LastStep.Values[0].MetricId;
        if (!string.Equals(installedMetricId, incomingMetricId, StringComparison.Ordinal))
        {
            changes.Add($"Suggests metric id {incomingMetricId} instead of {installedMetricId}.");
        }

        return new UpdateComparison(true, added.Count > 0 || removed.Count > 0, changes);
    }

    private static HashSet<string> Flatten(ImportReviewResult review) =>
        [.. review.Hosts.SelectMany(h => h.Sends.Select(s => $"{h.Host} receives {s}"))];
}
