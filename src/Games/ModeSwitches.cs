namespace Labs626.UrScore.Games;

/// <summary>
/// Which modes are on (spec "Mode switches"). A game has one switch and each of its modes has one; a mode reads only
/// when both are on. Pure: it is handed the stored map, so the settings page and the reader gate share one answer.
/// <para>
/// Only what the player chose is stored: a mode with no key follows its manifest <c>onByDefault</c>, and a game with no
/// key is on. Turning a game off writes nothing to the mode keys, so turning it back on restores exactly the modes the
/// player had.
/// </para>
/// </summary>
public sealed class ModeSwitches(GameCatalog catalog, IReadOnlyDictionary<string, bool>? modes)
{
    private readonly IReadOnlyDictionary<string, bool> _modes = modes ?? new Dictionary<string, bool>();

    /// <summary>A game's own switch, stored under its id. On unless the player turned it off.</summary>
    public bool IsGameOn(string gameId) => !_modes.TryGetValue(gameId, out var on) || on;

    /// <summary>The mode's own switch, ignoring its game: the player's choice, else the manifest default. False for an unknown key.</summary>
    public bool IsModeSet(string modeKey) =>
        _modes.TryGetValue(modeKey, out var on) ? on : catalog.Find(modeKey)?.OnByDefault ?? false;

    /// <summary>The mode reads: its game is on and its own switch is set. False for an unknown key.</summary>
    public bool IsOn(string modeKey)
    {
        var mode = catalog.Find(modeKey);
        return mode is not null && IsGameOn(mode.GameId) && IsModeSet(modeKey);
    }

    /// <summary>A recipe reads when the mode that names it is on. An orphan (no mode reads it) never does.</summary>
    public bool IsReaderOn(string slug) => catalog.ModeOf(slug) is { } mode && IsOn(mode.Key);

    /// <summary>A new map with only this key changed (a game id or a mode key). Other explicit keys stay; defaults stay implicit.</summary>
    public IReadOnlyDictionary<string, bool> With(string key, bool on) =>
        new Dictionary<string, bool>(_modes, StringComparer.Ordinal) { [key] = on };
}
