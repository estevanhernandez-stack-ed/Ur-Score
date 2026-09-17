using Labs626.UrScore.Board;
using Labs626.UrScore.Source;

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
/// anything sent (spec §6.3, stats design §7.2). Any other difference is listed in
/// <see cref="Changes"/> without asking.
/// </summary>
public sealed record UpdateComparison(bool IsUpdate, bool AsksAgain, IReadOnlyList<string> Changes);

/// <summary>
/// What a recipe would do on this PC, worked out before anything runs. Pure: no network, no disk
/// beyond the key lookup, so the safety screen is testable.
/// </summary>
public static class ImportReview
{
    /// <summary>
    /// Every account, not only those with Send on: Send controls what reaches RoRoRo, and a
    /// per-account source is asked about each account either way (stats design §7.1).
    /// </summary>
    public const string SendsUserIds = "the Roblox user id of every account in your RoRoRo list";

    public const string SendsNothing = "nothing about you";

    /// <summary>What Roblox's thumbnails service receives when a recipe has an icon.</summary>
    public const string ReceivesPictureId = "the picture's id, to find the icon";

    /// <summary>What Roblox's picture host does when a recipe has an icon. Rendered as "Sends the picture."</summary>
    public const string SendsThePicture = "sends the picture";

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

        // An icon's value is only known after a read, and an asset id is the case that contacts
        // Roblox's picture hosts, so both are named whenever the recipe has an icon.
        if (recipe.Icon is not null)
        {
            Add(IconClient.ThumbnailsHost, ReceivesPictureId);
            Add(IconClient.PictureHostShown, SendsThePicture);
        }

        // A host that receives something about you is not also "nothing about you".
        foreach (var list in sends.Values.Where(l => l.Count > 1))
        {
            list.Remove(SendsNothing);
        }

        return new ImportReviewResult([.. sends.Select(kv => new HostContact(kv.Key, kv.Value))], refusals, reused);
    }

    /// <summary>The sentence the import screen shows under a host.</summary>
    public static string SendsText(HostContact contact)
    {
        var received = contact.Sends.Where(s => s != SendsThePicture).ToList();
        var parts = new List<string>();
        if (received.Count > 0) parts.Add($"Receives {string.Join(", ", received)}.");
        if (contact.Sends.Contains(SendsThePicture)) parts.Add("Sends the picture.");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Stats design §7.2, one branch per row of its table. <paramref name="state"/> is the installed
    /// recipe's state: it says which stats are sent, and which counters were picked.
    /// </summary>
    public static UpdateComparison CompareToInstalled(Recipe? installed, Recipe incoming, IKeyStore keys, RecipeState? state = null)
    {
        if (installed is null) return new UpdateComparison(false, true, []);

        var before = Flatten(Review(installed, keys));
        var after = Flatten(Review(incoming, keys));

        var added = after.Except(before).ToList();
        var removed = before.Except(after).ToList();

        var changes = new List<string>();
        changes.AddRange(added.Select(x => $"New: {x}"));
        changes.AddRange(removed.Select(x => $"No longer: {x}"));
        var asks = added.Count > 0 || removed.Count > 0;

        if (state is { Stats: null, LegacyStatChoices.Count: > 0 })
        {
            changes.Add("Review which stats to show and send. The choices start with what this recipe sent before stat ticks were added.");
            asks = true;
        }

        if (installed.EffectiveEverySeconds != incoming.EffectiveEverySeconds)
        {
            changes.Add($"Polls every {incoming.EffectiveEverySeconds}s instead of {installed.EffectiveEverySeconds}s.");
        }

        if (installed.Period is null && incoming.Period is not null)
        {
            changes.Add($"Now tracks the current {RecipeWords.Period(incoming)}.");
        }
        else if (installed.Period is not null && incoming.Period is null)
        {
            changes.Add($"No longer tracks the current {RecipeWords.Period(installed)}.");
        }
        else if (installed.Period is { } beforePeriod && incoming.Period is { } afterPeriod
            && (beforePeriod with { Past = null }) != (afterPeriod with { Past = null }))
        {
            changes.Add($"Changes how the current {RecipeWords.Period(incoming)} is read.");
        }

        if (installed.Period?.Past is null && incoming.Period?.Past is not null)
        {
            changes.Add($"Now reads past {RecipeWords.Periods(incoming)}.");
        }
        else if (installed.Period?.Past is not null && incoming.Period?.Past is null)
        {
            changes.Add($"No longer reads past {RecipeWords.Periods(installed)}. Your score book is kept.");
        }
        else if (!string.Equals(installed.Period?.Past, incoming.Period?.Past, StringComparison.Ordinal))
        {
            changes.Add($"Past {RecipeWords.Periods(incoming)} are read from a different place.");
        }

        var choices = state?.ChoicesForUpdate ?? new Dictionary<string, StatChoice>();
        var oldStats = RecipeStats.Offered(installed, choices.Keys).ToDictionary(s => s.Key, StringComparer.Ordinal);
        var newStats = RecipeStats.Offered(incoming, choices.Keys).ToDictionary(s => s.Key, StringComparer.Ordinal);

        foreach (var stat in newStats.Values.Where(s => !oldStats.ContainsKey(s.Key)))
        {
            changes.Add($"New stat: {stat.Label}.");
        }

        foreach (var stat in oldStats.Values.Where(s => !newStats.ContainsKey(s.Key)))
        {
            if (choices.TryGetValue(stat.Key, out var choice) && choice.Send && !string.IsNullOrWhiteSpace(choice.MetricId))
            {
                changes.Add($"{stat.Label} will no longer be read, so RoRoRo stops getting {choice.MetricId.Trim()}.");
                asks = true;
            }
            else
            {
                changes.Add($"Removed stat: {stat.Label}.");
            }
        }

        foreach (var (key, now) in newStats.Where(kv => oldStats.ContainsKey(kv.Key)))
        {
            var was = oldStats[key];
            if (!string.Equals(was.Path, now.Path, StringComparison.Ordinal))
            {
                changes.Add($"{now.Label} is read from a different place.");
            }

            if (!string.Equals(was.SuggestedMetricId, now.SuggestedMetricId, StringComparison.Ordinal))
            {
                changes.Add($"Suggests {now.SuggestedMetricId} for {now.Label} instead of {was.SuggestedMetricId}.");
            }

            if (was.Count != now.Count)
            {
                changes.Add(now.Count ? $"{now.Label} now counts entries." : $"{now.Label} no longer counts entries.");
            }

            if (was.Format != now.Format)
            {
                changes.Add($"{now.Label} is shown as {FormatWords(now.Format)} instead of {FormatWords(was.Format)}.");
            }
        }

        if (installed.Icon is null && incoming.Icon is not null)
        {
            changes.Add("Adds an icon, which asks Roblox for the picture.");
            asks = true;
        }
        else if (installed.Icon is not null && incoming.Icon is null)
        {
            changes.Add("No longer shows an icon.");
        }
        else if (!string.Equals(installed.Icon, incoming.Icon, StringComparison.Ordinal))
        {
            changes.Add("The icon is read from a different place.");
        }

        if (MeaningChanged(installed, incoming, oldStats, newStats))
        {
            changes.Add("Changes what an empty answer means.");
        }

        return new UpdateComparison(true, asks, changes);
    }

    private static string FormatWords(StatFormat format) => format switch
    {
        StatFormat.Duration => "a duration",
        StatFormat.Date => "a date",
        _ => "a number",
    };

    /// <summary><c>absentMessage</c>, <c>unavailable</c>, <c>sum</c> or <c>placeLabel</c>: what the data means, never what happens with it.</summary>
    private static bool MeaningChanged(
        Recipe installed, Recipe incoming,
        IReadOnlyDictionary<string, RecipeStat> oldStats, IReadOnlyDictionary<string, RecipeStat> newStats) =>
        !installed.Steps.Select(s => s.AbsentMessage).SequenceEqual(incoming.Steps.Select(s => s.AbsentMessage))
        || installed.LastStep.Unavailable != incoming.LastStep.Unavailable
        || !string.Equals(installed.PlaceLabel, incoming.PlaceLabel, StringComparison.Ordinal)
        || !installed.Headline.Select(h => h.Sum).SequenceEqual(incoming.Headline.Select(h => h.Sum))
        || newStats.Any(kv => oldStats.TryGetValue(kv.Key, out var was) && was.Sum != kv.Value.Sum);

    /// <summary>What each host receives, as text. The icon's hosts are left to their own change line.</summary>
    private static HashSet<string> Flatten(ImportReviewResult review) =>
        [.. review.Hosts.SelectMany(h => h.Sends
            .Where(s => s != ReceivesPictureId && s != SendsThePicture)
            .Select(s => $"{h.Host} receives {s}"))];
}
