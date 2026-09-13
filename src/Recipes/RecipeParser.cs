using System.Text.Json;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

public sealed record RecipeParseResult(Recipe? Recipe, IReadOnlyList<string> Problems)
{
    public bool Ok => Recipe is not null;
}

/// <summary>
/// Recipe text to a <see cref="Recipe"/>, or every problem in it named (spec §6.5). Reads by hand
/// rather than deserializing, because a deserializer's exception names a byte offset and a person
/// sharing a recipe file needs "step 2 has no url".
/// </summary>
public static class RecipeParser
{
    public static RecipeParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            return Fail($"This file is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("A recipe is a JSON object, and this file is not one.");
            }

            // Version first and alone: a newer file's other fields may mean something this build cannot read.
            if (!TryInt(root, "recipe", out var version))
            {
                return Fail("No 'recipe' version number. A recipe file starts with \"recipe\": 1.");
            }

            if (version > Recipe.SupportedVersion)
            {
                return Fail($"This recipe was made by a newer Ur Score (format {version}). Update Ur Score to import it.");
            }

            if (version < 1)
            {
                return Fail($"'recipe' is {version}, and the first format is 1.");
            }

            var problems = new List<string>();
            var name = RequiredString(root, "name", "the recipe", problems);
            var credit = RequiredString(root, "credit", "the recipe", problems);
            var metricId = RequiredString(root, "metricId", "the recipe", problems);
            var author = OptionalString(root, "author");
            var valueLabel = OptionalString(root, "valueLabel") ?? "Value";

            if (!TryInt(root, "everySeconds", out var everySeconds) || everySeconds <= 0)
            {
                problems.Add("'everySeconds' must be a whole number of seconds above zero.");
            }

            var inputs = ParseInputs(root, problems);
            var keys = ParseKeys(root, problems);
            var steps = ParseSteps(root, problems);
            var headline = ParseHeadline(root, problems);

            // Cross-field checks only on a structurally complete recipe, so one missing url does not
            // cascade into three confusing follow-on complaints.
            if (problems.Count == 0)
            {
                Validate(inputs, keys, steps, headline, problems);
            }

            return problems.Count > 0
                ? new RecipeParseResult(null, problems)
                : new RecipeParseResult(
                    new Recipe(version, name!, credit!, author, metricId!, valueLabel, everySeconds,
                        inputs, keys, steps, headline),
                    []);
        }
    }

    private static RecipeParseResult Fail(string problem) => new(null, [problem]);

    private static bool TryInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return JsonNav.TryGet(obj, name, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out value);
    }

    private static string? OptionalString(JsonElement obj, string name) =>
        JsonNav.TryGet(obj, name, out var element)
        && element.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;

    private static string? RequiredString(JsonElement obj, string name, string where, List<string> problems)
    {
        var value = OptionalString(obj, name);
        if (value is null) problems.Add($"{Capitalize(where)} has no '{name}'.");
        return value;
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static IEnumerable<(int Number, JsonElement Item)> Items(JsonElement root, string name, List<string> problems)
    {
        if (!JsonNav.TryGet(root, name, out var array) || array.ValueKind == JsonValueKind.Null) yield break;

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"'{name}' must be a list.");
            yield break;
        }

        var number = 0;
        foreach (var item in array.EnumerateArray())
        {
            yield return (++number, item);
        }
    }

    private static void RequireHttps(string url, string what, List<string> problems)
    {
        if (!url.StartsWith(RecipeHosts.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{Capitalize(what)} must start with https://.");
            return;
        }

        var (host, hasUserInfo) = RecipeHosts.Authority(url);
        if (hasUserInfo)
        {
            problems.Add($"{Capitalize(what)} must not carry a username or password.");
        }

        if (host.Length == 0 || host.Contains('{') || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            problems.Add($"{Capitalize(what)} must name its host directly, not through a placeholder.");
        }

        if (host.Any(c => c > 127))
        {
            problems.Add($"{Capitalize(what)} names its host with non-ASCII characters. Write it in plain ASCII "
                + "(punycode) so the import screen shows the host that is actually contacted.");
        }
    }

    private static List<RecipeInput> ParseInputs(JsonElement root, List<string> problems)
    {
        var inputs = new List<RecipeInput>();
        foreach (var (number, item) in Items(root, "inputs", problems))
        {
            var where = $"input {number}";
            var id = RequiredString(item, "id", where, problems);
            var label = RequiredString(item, "label", where, problems);

            RecipeSearch? search = null;
            if (JsonNav.TryGet(item, "search", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                var url = RequiredString(s, "url", $"{where}'s search", problems);
                var list = RequiredString(s, "list", $"{where}'s search", problems);
                if (url is not null) RequireHttps(url, $"{where}'s search url", problems);
                if (url is not null && list is not null) search = new RecipeSearch(url, list);
            }

            if (id is not null && label is not null) inputs.Add(new RecipeInput(id, label, search));
        }

        return inputs;
    }

    private static List<RecipeKey> ParseKeys(JsonElement root, List<string> problems)
    {
        var keys = new List<RecipeKey>();
        foreach (var (number, item) in Items(root, "keys", problems))
        {
            var where = $"key {number}";
            var id = RequiredString(item, "id", where, problems);
            var label = RequiredString(item, "label", where, problems);
            var getOneAt = RequiredString(item, "getOneAt", where, problems);
            var placement = RequiredString(item, "in", where, problems);
            var headerOrParameter = RequiredString(item, "name", where, problems);

            if (getOneAt is not null) RequireHttps(getOneAt, $"{where}'s getOneAt", problems);

            KeyPlacement? parsed = placement?.ToLowerInvariant() switch
            {
                "header" => KeyPlacement.Header,
                "query" => KeyPlacement.Query,
                _ => null,
            };

            if (placement is not null && parsed is null)
            {
                problems.Add($"{Capitalize(where)}'s 'in' is '{placement}'. It must be header or query.");
            }

            if (id is not null && label is not null && getOneAt is not null && parsed is not null && headerOrParameter is not null)
            {
                keys.Add(new RecipeKey(id, label, getOneAt, parsed.Value, headerOrParameter));
            }
        }

        return keys;
    }

    private static List<RecipeStep> ParseSteps(JsonElement root, List<string> problems)
    {
        var steps = new List<RecipeStep>();
        if (!JsonNav.TryGet(root, "steps", out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0)
        {
            problems.Add("The recipe has no steps. It needs at least one request to make.");
            return steps;
        }

        var number = 0;
        foreach (var item in array.EnumerateArray())
        {
            var where = $"step {++number}";
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{Capitalize(where)} is not an object.");
                continue;
            }

            var url = RequiredString(item, "url", where, problems);
            if (url is not null) RequireHttps(url, $"{where}'s url", problems);

            var useKeys = new List<string>();
            if (JsonNav.TryGet(item, "useKeys", out var keyList) && keyList.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keyList.EnumerateArray())
                {
                    if (key.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(key.GetString()))
                    {
                        useKeys.Add(key.GetString()!.Trim());
                    }
                }
            }

            var take = new Dictionary<string, string>(StringComparer.Ordinal);
            if (JsonNav.TryGet(item, "take", out var takeObject) && takeObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in takeObject.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        take[property.Name] = property.Value.GetString()!.Trim();
                    }
                    else
                    {
                        problems.Add($"{Capitalize(where)} takes '{property.Name}' without a path.");
                    }
                }
            }

            var perAccount = JsonNav.TryGet(item, "perAccount", out var perAccountElement)
                             && perAccountElement.ValueKind == JsonValueKind.True;

            if (url is not null)
            {
                steps.Add(new RecipeStep(url, useKeys, take,
                    OptionalString(item, "idleWithout"), OptionalString(item, "idleMessage"),
                    OptionalString(item, "rows"), OptionalString(item, "userId"),
                    perAccount, OptionalString(item, "value")));
            }
        }

        return steps;
    }

    private static List<RecipeHeadline> ParseHeadline(JsonElement root, List<string> problems)
    {
        var headline = new List<RecipeHeadline>();
        foreach (var (number, item) in Items(root, "headline", problems))
        {
            var label = RequiredString(item, "label", $"headline {number}", problems);
            var path = RequiredString(item, "path", $"headline {number}", problems);
            if (label is not null && path is not null) headline.Add(new RecipeHeadline(label, path));
        }

        if (headline.Count > 2)
        {
            problems.Add($"The headline has {headline.Count} values. It shows at most two.");
        }

        return headline;
    }

    private static void Validate(
        List<RecipeInput> inputs, List<RecipeKey> keys, List<RecipeStep> steps,
        List<RecipeHeadline> headline, List<string> problems)
    {
        Duplicates(inputs.Select(i => i.Id), "input id", problems);
        Duplicates(keys.Select(k => k.Id), "key id", problems);

        var inputIds = inputs.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        if (inputIds.Contains(Placeholders.UserId))
        {
            problems.Add("An input cannot be called 'userId'. That name is reserved for your accounts' Roblox ids.");
        }

        var declaredKeys = keys.GroupBy(k => k.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var keyHosts = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var where = $"step {index + 1}";
            var isLast = index == steps.Count - 1;

            var known = new HashSet<string>(inputIds, StringComparer.Ordinal);
            known.UnionWith(taken);

            var urlNames = Placeholders.Names(step.Url);
            foreach (var name in urlNames)
            {
                if (name == Placeholders.UserId)
                {
                    if (!step.PerAccount) problems.Add($"{{userId}} can only be used in a perAccount step, and {where} is not one.");
                }
                else if (!known.Contains(name))
                {
                    problems.Add($"Unknown placeholder {{{name}}} in {where}'s url.");
                }
            }

            if (step.PerAccount && !urlNames.Contains(Placeholders.UserId))
            {
                problems.Add("A perAccount step's url must contain {userId}, or it asks the same thing once per account.");
            }

            foreach (var (label, path) in PathsOf(step))
            {
                foreach (var name in Placeholders.Names(path).Where(n => !known.Contains(n)))
                {
                    problems.Add($"Unknown placeholder {{{name}}} in {where}'s {label}.");
                }
            }

            foreach (var keyId in step.UseKeys)
            {
                if (!declaredKeys.ContainsKey(keyId))
                {
                    problems.Add($"{Capitalize(where)} uses key '{keyId}', which the recipe does not declare.");
                    continue;
                }

                var host = RecipeHosts.HostOf(step.Url);
                if (keyHosts.TryGetValue(keyId, out var other) && other != host)
                {
                    problems.Add($"Key '{keyId}' is sent to two hosts, {other} and {host}. A key can only go to one.");
                }
                else
                {
                    keyHosts[keyId] = host;
                }
            }

            if (step.IdleWithout is not null && !step.Take.ContainsKey(step.IdleWithout))
            {
                problems.Add($"{Capitalize(where)} is idle without '{step.IdleWithout}', which it does not take.");
            }

            var reads = step.Rows is not null || step.UserId is not null || step.Value is not null || step.PerAccount;
            if (!isLast && reads)
            {
                problems.Add($"{Capitalize(where)} reads a value, but only the last step can.");
            }

            if (isLast)
            {
                var listForm = step.Rows is not null && step.UserId is not null && step.Value is not null && !step.PerAccount;
                var perAccountForm = step.PerAccount && step.Value is not null && step.Rows is null && step.UserId is null;

                if (!listForm && !perAccountForm)
                {
                    problems.Add("The last step needs rows, userId and value, or perAccount and value.");
                }

                if (step.Take.Count > 0)
                {
                    problems.Add("The last step reads the number, so it cannot also take values for later steps.");
                }

                if (headline.Count > 0 && !listForm)
                {
                    problems.Add("A headline can only be read from a list-form last step.");
                }
            }

            foreach (var name in step.Take.Keys)
            {
                if (name == Placeholders.UserId)
                {
                    problems.Add($"{Capitalize(where)} takes 'userId', which is reserved for your accounts' Roblox ids.");
                }
                else if (inputIds.Contains(name))
                {
                    problems.Add($"{Capitalize(where)} takes '{name}', which is already the name of an input.");
                }

                taken.Add(name);
            }
        }

        var allKnown = new HashSet<string>(inputIds, StringComparer.Ordinal);
        allKnown.UnionWith(taken);
        for (var index = 0; index < headline.Count; index++)
        {
            foreach (var name in Placeholders.Names(headline[index].Path).Where(n => !allKnown.Contains(n)))
            {
                problems.Add($"Unknown placeholder {{{name}}} in headline {index + 1}'s path.");
            }
        }

        foreach (var input in inputs.Where(i => i.Search is not null))
        {
            foreach (var name in Placeholders.Names(input.Search!.Url).Concat(Placeholders.Names(input.Search.List)))
            {
                problems.Add($"Input '{input.Id}' searches with a placeholder {{{name}}}. A search list's address must be fixed.");
            }
        }
    }

    private static IEnumerable<(string Label, string Path)> PathsOf(RecipeStep step)
    {
        foreach (var (name, path) in step.Take) yield return ($"take path '{name}'", path);
        if (step.Rows is not null) yield return ("rows", step.Rows);
        if (step.UserId is not null) yield return ("userId", step.UserId);
        if (step.Value is not null) yield return ("value", step.Value);
    }

    private static void Duplicates(IEnumerable<string> ids, string what, List<string> problems)
    {
        foreach (var id in ids.GroupBy(i => i, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add($"The {what} '{id}' is used more than once.");
        }
    }
}
