using System.IO.Pipes;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Labs626.UrScore.Core;
using ROROROblox.PluginContract;

namespace Labs626.UrScore.Host;

/// <summary>
/// The pipe, the handshake, and the three calls this plugin is allowed to make.
/// <para>
/// Deliberately thin. Every judgement — what may be sent, which accounts map, what the response
/// meant — lives in the pure units under <c>Core/</c> and <c>Source/</c>, which is what makes them
/// testable without a running host.
/// </para>
/// </summary>
public sealed class HostClient(string pluginId) : IHostClient, IDisposable
{
    /// <summary>RoRoRo's plugin pipe. A literal because plugins reference only the contract
    /// package, never the app that defines the constant.</summary>
    public const string PipeName = "rororo-plugin-host";

    /// <summary>The WIRE contract version, which is not the NuGet package version. Package 0.10.0
    /// added ReportMetric additively and left this at "1.0", which is exactly why it broke no
    /// existing plugin. The host's handshake rejects a mismatch here with no negotiation.</summary>
    public const string ContractVersion = "1.0";

    /// <summary>
    /// Bounds every call. This runs unattended for hours, so a wedged pipe has to become a
    /// diagnosable state in the window rather than a hang with no explanation.
    /// </summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly Metadata _headers = new() { { "x-plugin-id", pluginId } };
    private readonly object _connectLock = new();
    private GrpcChannel? _channel;
    private RoRoRoHost.RoRoRoHostClient? _client;

    /// <summary>Whether a handshake has completed on the CURRENT connection. Tracked separately
    /// from <see cref="HostVersion"/> so a reconnect after the host dies can't be mistaken for an
    /// already-handshaken session just because the old version string is still sitting there.</summary>
    private bool _handshaken;

    private bool _disposed;

    /// <summary>The host's version, once a handshake has been accepted. Shown in the window.</summary>
    public string? HostVersion { get; private set; }

    /// <summary>Why the host refused us, if it did. Surfaced verbatim — a reject reason nobody
    /// reads is a dev cycle spent guessing.</summary>
    public string? RejectReason { get; private set; }

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = Connect();

            // GetHostInfo is ungated — it maps to no capability, so it answers before consent is
            // considered and makes a clean liveness probe.
            await client.GetHostInfoAsync(new Empty(), Options(cancellationToken)).ConfigureAwait(false);

            if (!_handshaken) await HandshakeAsync(cancellationToken).ConfigureAwait(false);
            return RejectReason is null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The pipe not existing yet, a connect that never completes, an RpcException from the
            // transport. To a caller that only wants to know whether to report, they are one
            // answer: not now. Drop the channel so the next attempt reconnects cleanly.
            Reset();
            return false;
        }
    }

    public async Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var list = await Connect()
                .GetAccountsAsync(new Empty(), Options(cancellationToken))
                .ConfigureAwait(false);

            return [.. list.Accounts.Select(a => new HostAccount(
                Guid.TryParse(a.AccountId, out var id) ? id : Guid.Empty,
                a.RobloxUserId,
                a.DisplayName))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Drop the channel so the next attempt reconnects and re-handshakes cleanly. Rethrow
            // rather than swallow: ReportPolicy counts what it sent, and a failure it never saw
            // would be counted as a success.
            Reset();
            throw;
        }
    }

    /// <summary>
    /// Do not call this directly — <c>ReportPolicy.SendAsync</c> is the only permitted caller, and
    /// <c>ReportPolicyTests</c> fails the build if anything else does.
    /// </summary>
    public async Task ReportMetricAsync(
        Guid subject, string metricId, double value, DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await Connect().ReportMetricAsync(new MetricReport
            {
                SubjectId = subject.ToString(),
                MetricId = metricId,
                Value = value,

                // ToUnixTimeMilliseconds truncates rather than rounds. The host's
                // FutureTolerance is 30 seconds, so a sub-millisecond truncation-versus-rounding
                // difference could never be what decides whether a report is dropped — this
                // isn't load-bearing. Truncating is still the correct direction, though: it can
                // only stamp a report earlier than "now", never later, so it can't be the reason
                // one tips into the host's future.
                ObservedAtUnixMs = observedAt.ToUnixTimeMilliseconds(),
            }, Options(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Drop the channel so the next attempt reconnects and re-handshakes cleanly. Rethrow
            // rather than swallow: ReportPolicy counts what it sent, and a failure it never saw
            // would be counted as a success.
            Reset();
            throw;
        }
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        // The first call after the pipe connects, and worth being accurate about: the host does
        // NOT gate anything on it. CapabilityInterceptor checks the capability map, the
        // x-plugin-id header and the consent record, and never tracks whether a handshake
        // happened — the host's own MetricSmoke reporter skips it entirely. We do it because it
        // is the contract's front door and because the reject reason is the fastest way to learn
        // a contract-version mismatch, not because anything downstream depends on it.
        var response = await Connect().HandshakeAsync(new HandshakeRequest
        {
            PluginId = pluginId,
            ContractVersion = ContractVersion,
        }, Options(cancellationToken)).ConfigureAwait(false);

        // Marked done for this connection either way — accepted or rejected, the handshake
        // itself completed, and a rejection this connection won't un-reject by asking again.
        // Only a Reset() (a fresh connection) clears this and earns another attempt.
        _handshaken = true;

        if (response.Accepted)
        {
            HostVersion = response.HostVersion;
            RejectReason = null;
        }
        else
        {
            HostVersion = null;
            RejectReason = string.IsNullOrWhiteSpace(response.RejectReason)
                ? "RoRoRo refused the connection without saying why."
                : response.RejectReason;
        }
    }

    private CallOptions Options(CancellationToken cancellationToken) =>
        new(headers: _headers, deadline: DateTime.UtcNow.Add(CallTimeout),
            cancellationToken: cancellationToken);

    private RoRoRoHost.RoRoRoHostClient Connect()
    {
        // Guards only the field checks and the (synchronous) channel/client construction below —
        // never an await — so a reachability check and a report racing each other with _client
        // still null build at most one channel between them instead of one each, with the loser's
        // leaked and undisposed.
        lock (_connectLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_client is not null) return _client;

            _channel = GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    ConnectTimeout = CallTimeout,
                    ConnectCallback = async (ctx, ct) =>
                    {
                        var pipe = new NamedPipeClientStream(
                            ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(ct).ConfigureAwait(false);
                        return pipe;
                    },
                },
            });

            return _client = new RoRoRoHost.RoRoRoHostClient(_channel);
        }
    }

    private void Reset()
    {
        lock (_connectLock)
        {
            _channel?.Dispose();
            _channel = null;
            _client = null;
            HostVersion = null;
            _handshaken = false;
        }
    }

    public void Dispose()
    {
        lock (_connectLock)
        {
            _disposed = true;
            _channel?.Dispose();
            _channel = null;
            _client = null;
            HostVersion = null;
            _handshaken = false;
        }
    }
}
