using System.Text.Json;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

public sealed record RecipeParseResult(Recipe? Recipe, IReadOnlyList<string> Problems)
{
    public bool Ok => Recipe is not null;
}

/// <summary>
/// Recipe text to a <see cref="Recipe"/>, or every problem in it named (spec §6.5, stats design §7.3).
/// Reads by hand rather than deserializing, because a deserializer's exception names a byte offset
/// and a person sharing a recipe file needs "step 2 has no url".
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
            var author = OptionalString(root, "author");
            var metricId = OptionalString(root, "metricId");
            var valueLabel = OptionalString(root, "valueLabel") ?? "Value";
            var icon = OptionalString(root, "icon");
            var placeLabel = OptionalString(root, "placeLabel");
            var groupsAreClans = OptionalBool(root, "groupsAreClans", false, "the recipe", problems);

            if (!TryInt(root, "everySeconds", out var everySeconds) || everySeconds <= 0)
            {
                problems.Add("'everySeconds' must be a whole number of seconds above zero.");
            }

            var inputs = ParseInputs(root, problems);
            var keys = ParseKeys(root, problems);
            var (steps, lastUsesValues) = ParseSteps(root, metricId, valueLabel, problems);
            var headline = ParseHeadline(root, problems);
            var period = ParsePeriod(root, problems);
            var groupList = steps.Count > 0 && steps[^1].GroupName is not null;

            // The top-level metricId only names a single 'value'. A 'values' list names each of its own.
            if (metricId is null && !lastUsesValues && !groupList)
            {
                problems.Add("The recipe has no 'metricId'.");
            }

            // Cross-field checks only on a structurally complete recipe, so one missing url does not
            // cascade into three confusing follow-on complaints.
            if (problems.Count == 0)
            {
                Validate(inputs, keys, steps, headline, icon, placeLabel, period, problems);
            }

            return problems.Count > 0
                ? new RecipeParseResult(null, problems)
                : new RecipeParseResult(
                    new Recipe(version, name!, credit!, author, everySeconds, inputs, keys, steps, headline,
                        icon, placeLabel ?? Recipe.DefaultPlaceLabel, period, groupsAreClans),
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

    /// <summary>Absent or null takes the fallback; anything but true or false is a named problem.</summary>
    private static bool OptionalBool(JsonElement obj, string name, bool fallback, string where, List<string> problems)
    {
        if (!JsonNav.TryGet(obj, name, out var element) || element.ValueKind == JsonValueKind.Null) return fallback;
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) return element.GetBoolean();

        problems.Add($"'{name}' in {where} must be true or false.");
        return fallback;
    }

    /// <summary>Present and not JSON null: the field was written, so it must be written correctly.</summary>
    private static bool Present(JsonElement obj, string name, out JsonElement element) =>
        JsonNav.TryGet(obj, name, out element) && element.ValueKind != JsonValueKind.Null;

    /// <summary>Absent or null reads as a number; otherwise exactly "number", "duration" (seconds) or "date" (unix seconds), else a named problem.</summary>
    private static StatFormat ParseFormat(JsonElement item, string where, List<string> problems)
    {
        if (!Present(item, "format", out var element)) return StatFormat.Number;

        switch (element.ValueKind == JsonValueKind.String ? element.GetString() : null)
        {
            case "number": return StatFormat.Number;
            case "duration": return StatFormat.Duration;
            case "date": return StatFormat.Date;
            default:
                problems.Add($"'format' in {where} must be \"number\", \"duration\" or \"date\".");
                return StatFormat.Number;
        }
    }

    /// <summary>Absent or null is none; anything but text with something in it is a named problem. Trimmed.</summary>
    private static string? OptionalText(JsonElement obj, string name, string where, List<string> problems)
    {
        if (!Present(obj, name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())) return element.GetString()!.Trim();

        problems.Add($"'{name}' in {where} must be text.");
        return null;
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

            if (id is not null && label is not null) inputs.Add(new RecipeInput(id, label, search, OptionalString(item, "plural")));
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

    /// <returns>The steps, and whether the last one lists its stats under 'values'.</returns>
    private static (List<RecipeStep> Steps, bool LastUsesValues) ParseSteps(
        JsonElement root, string? metricId, string valueLabel, List<string> problems)
    {
        var steps = new List<RecipeStep>();
        if (!JsonNav.TryGet(root, "steps", out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0)
        {
            problems.Add("The recipe has no steps. It needs at least one request to make.");
            return (steps, false);
        }

        var number = 0;
        var usesValues = false;
        foreach (var item in array.EnumerateArray())
        {
            var where = $"step {++number}";
            usesValues = false;
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

            var value = OptionalString(item, "value");
            usesValues = Present(item, "values", out var valuesElement);
            if (usesValues && value is not null)
            {
                problems.Add($"{Capitalize(where)} has both 'value' and 'values'. Use 'values' for several stats, or 'value' for one.");
            }

            var values = usesValues
                ? ParseValues(valuesElement, where, problems)
                : value is not null
                    ? [new RecipeValue(RecipeValue.ShorthandId, valueLabel, value, metricId ?? "")]
                    : new List<RecipeValue>();

            if (url is not null)
            {
                steps.Add(new RecipeStep(url, useKeys, take,
                    OptionalString(item, "idleWithout"), OptionalString(item, "idleMessage"),
                    OptionalString(item, "rows"), OptionalString(item, "userId"),
                    perAccount, values,
                    ParseCounters(item, where, problems),
                    ParseUnavailable(item, where, problems),
                    ParseAbsentMessage(item, where, problems),
                    OptionalString(item, "groupName"),
                    OptionalString(item, "rank"),
                    ParseAsOf(item, where, problems)));
            }
        }

        return (steps, usesValues);
    }

    private static List<RecipeValue> ParseValues(JsonElement array, string where, List<string> problems)
    {
        var values = new List<RecipeValue>();
        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{Capitalize(where)}'s 'values' must be a list.");
            return values;
        }

        if (array.GetArrayLength() == 0)
        {
            problems.Add($"{Capitalize(where)}'s 'values' is empty. List at least one stat.");
            return values;
        }

        var number = 0;
        foreach (var item in array.EnumerateArray())
        {
            var valueWhere = $"{where}'s value {++number}";
            var id = RequiredString(item, "id", valueWhere, problems);
            var label = RequiredString(item, "label", valueWhere, problems);
            var path = RequiredString(item, "path", valueWhere, problems);
            var metricId = RequiredString(item, "metricId", valueWhere, problems);
            var sum = OptionalBool(item, "sum", true, valueWhere, problems);
            var count = OptionalBool(item, "count", false, valueWhere, problems);
            var show = OptionalBool(item, "show", false, valueWhere, problems);
            var format = ParseFormat(item, valueWhere, problems);
            var section = OptionalText(item, "section", valueWhere, problems);

            if (count && format != StatFormat.Number)
            {
                problems.Add($"{Capitalize(valueWhere)} counts entries, so its format can only be \"number\".");
            }

            if (format == StatFormat.Date && sum)
            {
                problems.Add($"{Capitalize(valueWhere)} is a date, and dates can't be added up. Give it \"sum\": false.");
            }

            if (id is not null && label is not null && path is not null && metricId is not null)
            {
                values.Add(new RecipeValue(id, label, path, metricId, sum, count, format, show, section));
            }
        }

        return values;
    }

    private static RecipeCounters? ParseCounters(JsonElement step, string where, List<string> problems)
    {
        if (!Present(step, "counters", out var counters)) return null;

        if (counters.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{Capitalize(where)}'s 'counters' must be an object with a label, a path and a metricIdPrefix.");
            return null;
        }

        var label = RequiredString(counters, "label", $"{where}'s counters", problems);
        var path = RequiredString(counters, "path", $"{where}'s counters", problems);
        var prefix = RequiredString(counters, "metricIdPrefix", $"{where}'s counters", problems);

        return label is not null && path is not null && prefix is not null ? new RecipeCounters(label, path, prefix) : null;
    }

    private static RecipeUnavailable? ParseUnavailable(JsonElement step, string where, List<string> problems)
    {
        if (!Present(step, "unavailable", out var unavailable)) return null;

        if (unavailable.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{Capitalize(where)}'s 'unavailable' must be an object with a path, an 'is' and a message.");
            return null;
        }

        var path = RequiredString(unavailable, "path", $"{where}'s unavailable", problems);
        var message = RequiredString(unavailable, "message", $"{where}'s unavailable", problems);

        string? isText = null;
        var isKind = JsonValueKind.Undefined;
        if (JsonNav.TryGet(unavailable, "is", out var isElement))
        {
            isKind = isElement.ValueKind;
            isText = isKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => isElement.GetRawText(),
                JsonValueKind.String => isElement.GetString(),
                _ => null,
            };
        }

        if (isText is null)
        {
            problems.Add($"{Capitalize(where)}'s unavailable has no 'is'. It must be true, false, a number or text.");
        }

        return path is not null && message is not null && isText is not null
            ? new RecipeUnavailable(path, isKind, isText, message)
            : null;
    }

    private static string? ParseAbsentMessage(JsonElement step, string where, List<string> problems)
    {
        if (!Present(step, "absentMessage", out var element)) return null;

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            problems.Add($"{Capitalize(where)}'s 'absentMessage' must be text.");
            return null;
        }

        return element.GetString()!.Trim();
    }

    private static RecipePeriod? ParsePeriod(JsonElement root, List<string> problems)
    {
        if (!Present(root, "period", out var period)) return null;

        if (period.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'period' must be an object with a value, and optionally starts, ends and past.");
            return null;
        }

        var value = OptionalString(period, "value");
        if (value is null)
        {
            problems.Add("The period has no 'value'.");
            return null;
        }

        return new RecipePeriod(value, OptionalString(period, "starts"), OptionalString(period, "ends"), OptionalString(period, "past"));
    }

    private static RecipeAsOf? ParseAsOf(JsonElement step, string where, List<string> problems)
    {
        if (!Present(step, "asOf", out var asOf)) return null;

        var time = asOf.ValueKind == JsonValueKind.Object ? OptionalString(asOf, "time") : null;
        if (time is null)
        {
            problems.Add($"{Capitalize(where)}'s 'asOf' must be an object with a 'time' path.");
            return null;
        }

        return new RecipeAsOf(time, OptionalString(asOf, "stale"));
    }

    private static List<RecipeHeadline> ParseHeadline(JsonElement root, List<string> problems)
    {
        var headline = new List<RecipeHeadline>();
        foreach (var (number, item) in Items(root, "headline", problems))
        {
            var label = RequiredString(item, "label", $"headline {number}", problems);
            var path = RequiredString(item, "path", $"headline {number}", problems);
            var sum = OptionalBool(item, "sum", true, $"headline {number}", problems);
            var id = OptionalString(item, "id") ?? (label is null ? null : RecipeStats.Slug(label));
            if (label is not null && path is not null) headline.Add(new RecipeHeadline(label, path, sum, id!));
        }

        if (headline.Count > 2)
        {
            problems.Add($"The headline has {headline.Count} values. It shows at most two.");
        }

        return headline;
    }

    private static void Validate(
        List<RecipeInput> inputs, List<RecipeKey> keys, List<RecipeStep> steps,
        List<RecipeHeadline> headline, string? icon, string? placeLabel, RecipePeriod? period, List<string> problems)
    {
        Duplicates(inputs.Select(i => i.Id), "input id", problems);
        Duplicates(keys.Select(k => k.Id), "key id", problems);
        Duplicates(headline.Select(h => h.Id), "headline id", problems);

        var inputIds = inputs.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        if (inputIds.Contains(Placeholders.UserId))
        {
            problems.Add("An input cannot be called 'userId'. That name is reserved for your accounts' Roblox ids.");
        }

        var declaredKeys = keys.GroupBy(k => k.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var keyHosts = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var listForm = false;
        var groupForm = false;

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

            if (step.Unavailable is not null && !step.PerAccount)
            {
                problems.Add($"{Capitalize(where)} has 'unavailable', but only a perAccount step can.");
            }

            if (step.AsOf is not null && !isLast)
            {
                problems.Add($"{Capitalize(where)} has 'asOf', but only the last step can.");
            }

            if (step.GroupName is not null && step.UserId is not null)
            {
                problems.Add($"{Capitalize(where)} has both 'userId' and 'groupName'. A row is a player or a group, not both.");
            }

            if (step.Rank is not null && step.GroupName is null)
            {
                problems.Add($"{Capitalize(where)} has 'rank', which only a groupName step can use.");
            }

            var reads = step.Rows is not null || step.UserId is not null || step.Values.Count > 0
                        || step.Counters is not null || step.PerAccount
                        || step.GroupName is not null || step.Rank is not null;
            if (!isLast && reads)
            {
                problems.Add($"{Capitalize(where)} reads a value, but only the last step can.");
            }

            if (isLast)
            {
                listForm = step.Rows is not null && step.UserId is not null && step.GroupName is null && step.Values.Count > 0 && !step.PerAccount;
                groupForm = step.Rows is not null && step.GroupName is not null && step.UserId is null && step.Values.Count > 0 && !step.PerAccount;
                var perAccountForm = step.PerAccount && step.Values.Count > 0 && step.Rows is null && step.UserId is null && step.GroupName is null;

                if (!listForm && !perAccountForm && !groupForm && step.GroupName is null)
                {
                    problems.Add("The last step needs rows, userId and value, or perAccount and value.");
                }
                else if (!groupForm && step.GroupName is not null && step.UserId is null)
                {
                    problems.Add("A groupName step needs rows and a value, and can't be perAccount.");
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

            foreach (var id in step.Values.GroupBy(v => v.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                problems.Add($"{Capitalize(where)} uses the value id '{id}' more than once.");
            }

            foreach (var id in step.Values.Select(v => v.Id).Where(id => id.StartsWith(RecipeCounters.KeyPrefix, StringComparison.Ordinal)))
            {
                problems.Add($"{Capitalize(where)}'s value id '{id}' starts with '{RecipeCounters.KeyPrefix}', which is kept for statistics picked from counters.");
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

        foreach (var metricId in steps.SelectMany(s => s.Values).GroupBy(v => v.MetricId, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add($"The metricId '{metricId}' is suggested for more than one value. Each stat needs its own.");
        }

        if (icon is not null && !listForm)
        {
            problems.Add("An icon can only be read from a list-form last step.");
        }

        if (placeLabel is not null && !listForm)
        {
            problems.Add("A placeLabel only applies to a list-form last step.");
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

        foreach (var name in Placeholders.Names(icon ?? "").Where(n => !allKnown.Contains(n)))
        {
            problems.Add($"Unknown placeholder {{{name}}} in the icon path.");
        }

        foreach (var input in inputs.Where(i => i.Search is not null))
        {
            foreach (var name in Placeholders.Names(input.Search!.Url).Concat(Placeholders.Names(input.Search.List)))
            {
                problems.Add($"Input '{input.Id}' searches with a placeholder {{{name}}}. A search list's address must be fixed.");
            }
        }

        if (period is not null)
        {
            if (!taken.Contains(period.Value))
            {
                problems.Add($"The period's value '{period.Value}' is not something an earlier step takes.");
            }

            foreach (var (name, what) in new[] { (period.Starts, "starts"), (period.Ends, "ends") })
            {
                if (name is not null && !taken.Contains(name))
                {
                    problems.Add($"The period's {what} '{name}' is not something an earlier step takes.");
                }
            }

            if (period.Past is not null)
            {
                if (!listForm) problems.Add("A period's 'past' needs a last step with rows and userId.");

                foreach (var name in Placeholders.Names(period.Past).Where(n => !allKnown.Contains(n)))
                {
                    problems.Add($"Unknown placeholder {{{name}}} in the period's past path.");
                }
            }
        }

        // Score book spec §3.6: no path may name a particular player.
        var everyPath = steps.SelectMany((step, index) => PathsOf(step).Select(p => (Where: $"step {index + 1}", Path: p.Path)))
            .Concat(headline.Select((h, index) => (Where: $"headline {index + 1}", Path: h.Path)))
            .Concat(icon is null ? [] : new[] { (Where: "the icon path", Path: icon) })
            .Concat(period?.Past is null ? [] : new[] { (Where: "the period's past path", Path: period.Past) });

        foreach (var (where, path) in everyPath.Where(p => PathRules.HasLiteralNumber(p.Path)))
        {
            problems.Add($"{Capitalize(where)}: '{path}' names a number. Recipes can't point at a particular player; use a placeholder instead.");
        }
    }

    private static IEnumerable<(string Label, string Path)> PathsOf(RecipeStep step)
    {
        foreach (var (name, path) in step.Take) yield return ($"take path '{name}'", path);
        if (step.Rows is not null) yield return ("rows", step.Rows);
        if (step.UserId is not null) yield return ("userId", step.UserId);
        foreach (var value in step.Values) yield return ($"value '{value.Id}'", value.Path);
        if (step.Counters is not null) yield return ("counters path", step.Counters.Path);
        if (step.Unavailable is not null) yield return ("unavailable path", step.Unavailable.Path);
        if (step.GroupName is not null) yield return ("groupName", step.GroupName);
        if (step.Rank is not null) yield return ("rank", step.Rank);
        if (step.AsOf is not null) yield return ("asOf time", step.AsOf.Time);
        if (step.AsOf?.Stale is not null) yield return ("asOf stale", step.AsOf.Stale);
    }

    private static void Duplicates(IEnumerable<string> ids, string what, List<string> problems)
    {
        foreach (var id in ids.GroupBy(i => i, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add($"The {what} '{id}' is used more than once.");
        }
    }
}
