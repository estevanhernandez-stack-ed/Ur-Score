using System.Collections.Concurrent;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// Several sources share one host (score book spec §4.2). Requests to a host go one at a time and at least
/// <c>spacing</c> apart, so five clan watches are five calls in turn, never a burst against someone else's API.
/// </summary>
public sealed class SpacedTransport(IRecipeTransport inner, TimeProvider time, TimeSpan spacing) : IRecipeTransport
{
    public static readonly TimeSpan DefaultSpacing = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, Lane> _lanes = new(StringComparer.OrdinalIgnoreCase);

    public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
    {
        var lane = _lanes.GetOrAdd(url.Host, _ => new Lane());
        await lane.One.WaitAsync(cancellationToken).ConfigureAwait(false);
        var sent = false;
        try
        {
            var wait = lane.LastFinished + spacing - time.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);

            sent = true;
            return await inner.GetAsync(url, headers, label, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Only a request that actually LEFT touches the host's clock. One cancelled while still waiting its
            // turn never reached anybody's server, and counting it made the next caller wait a full spacing for
            // nothing (S1-4.2). A request that left and then failed still counts: it did reach the server, and
            // the spacing exists to protect the server, not to reward us for a good answer.
            if (sent) lane.LastFinished = time.GetUtcNow();
            lane.One.Release();
        }
    }

    private sealed class Lane
    {
        public readonly SemaphoreSlim One = new(1, 1);

        public DateTimeOffset LastFinished = DateTimeOffset.MinValue;
    }
}
