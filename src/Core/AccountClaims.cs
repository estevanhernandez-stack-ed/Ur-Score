namespace Labs626.UrScore.Core;

/// <summary>
/// Ruling R6 (score book spec §4.2): when two sources of the same recipe both see one of your accounts,
/// such as an alt mid-way through a clan switch, the account belongs to whichever source claimed it first,
/// until that claim is older than the window. One account is then sent and kept once per recipe.
/// </summary>
public sealed class AccountClaims(TimeProvider time)
{
    private readonly object _gate = new();

    private readonly Dictionary<(string Recipe, long UserId), (string SourceId, DateTimeOffset At)> _claims = [];

    public bool TryClaim(string recipe, long userId, string sourceId, TimeSpan window)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_claims.TryGetValue((recipe, userId), out var claim) && claim.SourceId != sourceId && now - claim.At < window)
            {
                return false;
            }

            _claims[(recipe, userId)] = (sourceId, now);
            return true;
        }
    }
}
