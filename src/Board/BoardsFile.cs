using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Board;

/// <summary>What loading <c>boards.json</c> found: a file that isn't there is not the same as one that can't be read.</summary>
public sealed record BoardsLoad(IReadOnlyList<BoardDef> Boards, bool Exists, bool Readable);

/// <summary>
/// <c>boards.json</c> (spec §9.2). Written to a temp file and moved over, so a crash mid-write leaves the old
/// file. Repair is per panel (R4); malformed JSON is unreadable and its text is kept on the next save (R3).
/// </summary>
public sealed class BoardsFile(string path, TimeProvider time)
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

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "boards.json");

    public BoardsLoad Load()
    {
        try
        {
            if (!File.Exists(path)) return new BoardsLoad([], Exists: false, Readable: true);
            return new BoardsLoad(Parse(File.ReadAllText(path)), Exists: true, Readable: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new BoardsLoad([], Exists: true, Readable: false);
        }
    }

    /// <summary>
    /// Writes the boards. Returns where the old file was kept, or null. The old file is kept when it doesn't
    /// parse, or, with <paramref name="keepExisting"/>, whatever it holds: a file that couldn't be read at start
    /// may read now, and was still never shown (R3). Throws when the folder can't be written.
    /// </summary>
    public string? Save(IReadOnlyList<BoardDef> boards, bool keepExisting = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var kept = KeepUnreadable(keepExisting);

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(boards));
        File.Move(temp, path, overwrite: true);
        return kept;
    }

    public static string Serialize(IReadOnlyList<BoardDef> boards) =>
        JsonSerializer.Serialize(boards.Select(board => new BoardDto
        {
            Id = board.Id,
            Name = board.Name,
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

    public static IReadOnlyList<BoardDef> Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<BoardDto?>>(json, Options) ?? new List<BoardDto?>();
        var boards = new List<BoardDef>();
        var boardIds = new HashSet<string>(StringComparer.Ordinal);
        var panelIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dto in dtos.OfType<BoardDto>())
        {
            var boardId = UniqueId(dto.Id, boardIds, BoardDefs.NewBoardId);
            var panels = new List<PanelDef>();

            foreach (var panel in (dto.Panels ?? new List<PanelDto?>()).OfType<PanelDto>().OrderBy(p => p.Order))
            {
                if (TypeOf(panel.Type) is not { } type) continue;

                panels.Add(new PanelDef(
                    UniqueId(panel.Id, panelIds, BoardDefs.NewPanelId),
                    type,
                    panel.Size is { Span: > 0 } size ? new PanelSize(Math.Clamp(size.Span, 1, BoardLayout.Columns), size.Tall) : BoardDefs.DefaultSize(type),
                    CleanSettings(panel.Settings),
                    ValidPopOut(panel.Popout)));
            }

            boards.Add(new BoardDef(boardId, BoardDefs.CleanName(dto.Name) ?? $"Board {boards.Count + 1}", panels));
        }

        return boards;
    }

    /// <summary>A file that no longer parses, or any file when asked to keep it, is copied aside before it is written over (R3).</summary>
    private string? KeepUnreadable(bool keepExisting)
    {
        if (!File.Exists(path)) return null;

        var text = File.ReadAllText(path);
        if (!keepExisting && Parses(text)) return null;

        var stamp = time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var copy = Path.Combine(Path.GetDirectoryName(path)!, $"boards.unreadable-{stamp}.json");
        File.WriteAllText(copy, text);
        return copy;
    }

    private static bool Parses(string text)
    {
        try
        {
            Parse(text);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>A known type by name only: a number or a flags list could name a type by accident.</summary>
    private static PanelType? TypeOf(string? text) =>
        text is { Length: > 0 } && char.IsAsciiLetter(text[0]) && !text.Contains(',')
        && Enum.TryParse<PanelType>(text, ignoreCase: true, out var type) && Enum.IsDefined(type)
            ? type
            : null;

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
