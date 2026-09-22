using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Board;

/// <summary>
/// The boards' JSON shape, on its own. <see cref="BoardsFile"/> writes and reads boards.json through this; a
/// stats file carries a setup's boards through this too, without naming the boards-file writer — the fence
/// around boards.json (<c>BoardsFileFenceTests</c>) reserves that name for the one place that writes the file.
/// The shape is unchanged from what BoardsFile wrote before 2026-09-22.
/// </summary>
public static class BoardJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(IReadOnlyList<BoardDef> boards) =>
        JsonSerializer.Serialize(boards.Select(board => new BoardDto
        {
            Id = board.Id,
            Name = board.Name,
            Follows = board.Follows,
            Panels = [.. board.Panels.Select((panel, index) => new PanelDto
            {
                Id = panel.Id,
                Type = JsonNamingPolicy.CamelCase.ConvertName(panel.Type.ToString()),
                Size = new SizeDto { Span = panel.Size.Span, Tall = panel.Size.Tall },
                Order = index,
                Settings = panel.Settings,
                Popout = panel.PopOut,
            })],
        }).ToList(), Options);

    /// <summary>
    /// The boards in <paramref name="json"/>, repaired per panel (R4). Text that isn't JSON, or isn't a list, throws
    /// <see cref="JsonException"/> (R3). Inside the list a value of the wrong JSON type costs only what holds it: a
    /// board's id, name or panel list, or a panel's size, order or pop-out, falls back as if it were missing; a panel
    /// whose type or settings don't read is dropped; an entry that isn't a board is skipped.
    /// </summary>
    public static IReadOnlyList<BoardDef> Parse(string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return [];
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("boards.json is not a list of boards.");

        var boards = new List<BoardDef>();
        var boardIds = new HashSet<string>(StringComparer.Ordinal);
        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        var followed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var board in root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
        {
            var boardId = UniqueId(Text(board, "id"), boardIds, BoardDefs.NewBoardId);
            var panels = new List<PanelDef>();

            var entries = Property(board, "panels") is { ValueKind: JsonValueKind.Array } list
                ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                : [];
            foreach (var panel in entries.OrderBy(p => Number(p, "order") ?? 0))
            {
                if (TypeOf(Text(panel, "type")) is not { } type || ReadSettings(panel) is not { } settings) continue;

                panels.Add(new PanelDef(
                    UniqueId(Text(panel, "id"), panelIds, BoardDefs.NewPanelId),
                    type,
                    ReadSize(panel, type),
                    settings,
                    ReadPopOut(panel)));
            }

            // A board follows a starter Ur Score knows, and only the first board following it does (D2).
            var follows = FollowsOf(Text(board, "follows"));
            if (follows is not null && !followed.Add(follows)) follows = null;

            boards.Add(new BoardDef(boardId, BoardDefs.CleanName(Text(board, "name")) ?? $"Board {boards.Count + 1}", panels, follows));
        }

        return boards;
    }

    /// <summary>A known type by name only: a number or a flags list could name a type by accident.</summary>
    private static PanelType? TypeOf(string? text) =>
        text is { Length: > 0 } && char.IsAsciiLetter(text[0]) && !text.Contains(',')
        && Enum.TryParse<PanelType>(text, ignoreCase: true, out var type) && Enum.IsDefined(type)
            ? type
            : null;

    /// <summary>The starter a board follows, as its key; anything else follows nothing.</summary>
    private static string? FollowsOf(string? text) =>
        StarterBoards.Names.Select(StarterBoards.KeyOf).FirstOrDefault(key => string.Equals(key, text?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string UniqueId(string? given, HashSet<string> taken, Func<string> fresh)
    {
        var id = given?.Trim();
        if (!string.IsNullOrEmpty(id) && taken.Add(id)) return id;

        do
        {
            id = fresh();
        }
        while (!taken.Add(id));

        return id;
    }

    /// <summary>A panel's settings: missing or null reads as none; any other value that doesn't read as settings is null, and drops the panel.</summary>
    private static PanelSettings? ReadSettings(JsonElement panel)
    {
        if (Property(panel, "settings") is not { ValueKind: not JsonValueKind.Null } element) return CleanSettings(null);
        return TryRead<PanelSettings>(element, out var settings) ? CleanSettings(settings) : null;
    }

    /// <summary>A span of 1 or more, clamped to the grid; else, whatever the size holds, the type's default size.</summary>
    private static PanelSize ReadSize(JsonElement panel, PanelType type) =>
        Property(panel, "size") is { ValueKind: JsonValueKind.Object } size && Number(size, "span") is int span and > 0
            ? new PanelSize(Math.Clamp(span, 1, BoardLayout.Columns), Property(size, "tall")?.ValueKind == JsonValueKind.True)
            : BoardDefs.DefaultSize(type);

    /// <summary>A pop-out with a finite place and a size (R4); one that doesn't read as a place has none.</summary>
    private static PopOutRect? ReadPopOut(JsonElement panel) =>
        Property(panel, "popout") is { } element && TryRead<PopOutRect>(element, out var rect) ? ValidPopOut(rect) : null;

    /// <summary>The value, the last of that name in any letter case, as the serializer reads it; null when there is none.</summary>
    private static JsonElement? Property(JsonElement element, string name)
    {
        JsonElement? found = null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) found = property.Value;
        }

        return found;
    }

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var value) ? value : null;

    /// <summary>One value as a <typeparamref name="T"/>; false when it doesn't read as one, so only what holds it is lost.</summary>
    private static bool TryRead<T>(JsonElement element, out T? value)
    {
        try
        {
            value = element.Deserialize<T>(Options);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            value = default;
            return false;
        }
    }

    private static PanelSettings CleanSettings(PanelSettings? settings) =>
        settings is null
            ? new PanelSettings()
            : settings with
            {
                Recipe = settings.Recipe ?? "",
                SourceIds = settings.SourceIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            };

    private static PopOutRect? ValidPopOut(PopOutRect? rect) =>
        rect is { W: > 0.0, H: > 0.0 } r && double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.W) && double.IsFinite(r.H)
            ? r
            : null;

    private sealed class BoardDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Follows { get; set; }

        public List<PanelDto?>? Panels { get; set; }
    }

    private sealed class PanelDto
    {
        public string? Id { get; set; }

        public string? Type { get; set; }

        public SizeDto? Size { get; set; }

        public int Order { get; set; }

        public PanelSettings? Settings { get; set; }

        public PopOutRect? Popout { get; set; }
    }

    private sealed class SizeDto
    {
        public int Span { get; set; }

        public bool Tall { get; set; }
    }
}
