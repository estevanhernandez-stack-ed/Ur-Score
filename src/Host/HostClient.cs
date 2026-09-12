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
    private GrpcChannel? _channel;
    private RoRoRoHost.RoRoRoHostClient? _client;

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

            if (HostVersion is null) await HandshakeAsync(cancellationToken).ConfigureAwait(false);
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
        var list = await Connect()
            .GetAccountsAsync(new Empty(), Options(cancellationToken))
            .ConfigureAwait(false);

        return [.. list.Accounts.Select(a => new HostAccount(
            Guid.TryParse(a.AccountId, out var id) ? id : Guid.Empty,
            a.RobloxUserId,
            a.DisplayName))];
    }

    /// <summary>
    /// Do not call this directly — <c>ReportPolicy.SendAsync</c> is the only permitted caller, and
    /// <c>ReportPolicyTests</c> fails the build if anything else does.
    /// </summary>
    public async Task ReportMetricAsync(
        Guid subject, string metricId, double value, DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await Connect().ReportMetricAsync(new MetricReport
        {
            SubjectId = subject.ToString(),
            MetricId = metricId,
            Value = value,

            // ToUnixTimeMilliseconds truncates rather than rounds, which is the direction that
            // matters: the host drops anything stamped in its own future, and rounding up on an
            // observation made right now could tip it over by a fraction of a millisecond.
            ObservedAtUnixMs = observedAt.ToUnixTimeMilliseconds(),
        }, Options(cancellationToken)).ConfigureAwait(false);
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        // The first call after the pipe connects. Until it is accepted, every gated RPC fails.
        var response = await Connect().HandshakeAsync(new HandshakeRequest
        {
            PluginId = pluginId,
            ContractVersion = ContractVersion,
        }, Options(cancellationToken)).ConfigureAwait(false);

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

    private void Reset()
    {
        _channel?.Dispose();
        _channel = null;
        _client = null;
        HostVersion = null;
    }

    public void Dispose() => Reset();
}
