using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.Core;

namespace Rpc.SignalR.Client;

public sealed class RpcConnectionHost(
    IOptions<RpcClientOptions> options,
    IDispatcherRegistry dispatchers,
    IRpcIdentityProvider identityProvider,
    ILogger<RpcConnectionHost> logger) : IHostedService, IAsyncDisposable
{
    private readonly RpcClientOptions _options = options.Value;
    private readonly AgentDuplexSessions _agentSessions = new();
    private HubConnection? _connection;

    public HubConnection Connection => _connection
        ?? throw new InvalidOperationException("RpcConnectionHost has not started yet.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var url = await BuildUrlAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("RPC client connecting to {Url}", url);

        var builder = new HubConnectionBuilder()
            .WithUrl(url, http =>
            {
                if (_options.AccessTokenProvider is not null)
                    http.AccessTokenProvider = _options.AccessTokenProvider;
            })
            .WithAutomaticReconnect();

        _connection = builder.Build();
        WireUpHandlers(_connection);
        await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("RPC client connected (state={State})", _connection.State);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is null) return;
        try { await _connection.StopAsync(cancellationToken).ConfigureAwait(false); } catch { /* swallow */ }
    }

    /// <summary>
    /// Fecha a conexão atual e abre uma nova. Re-chama os providers de
    /// DeviceId/UserId/TenantId/Capabilities/AccessToken — útil após login/logout pra
    /// trocar a identidade declarada no handshake.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync().ConfigureAwait(false); } catch { /* swallow */ }
            _connection = null;
        }
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private async Task<string> BuildUrlAsync(CancellationToken cancellationToken)
    {
        var identity = await identityProvider.GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(identity.DeviceId))
            qs.Add("deviceId=" + Uri.EscapeDataString(identity.DeviceId));
        if (!string.IsNullOrWhiteSpace(identity.TenantId))
            qs.Add("tenantId=" + Uri.EscapeDataString(identity.TenantId));
        if (!string.IsNullOrWhiteSpace(identity.UserId))
            qs.Add("userId=" + Uri.EscapeDataString(identity.UserId));
        var caps = identity.CapabilitiesOrEmpty;
        if (caps.Count > 0)
            qs.Add("caps=" + Uri.EscapeDataString(string.Join(",", caps.Select(c => c.Name))));
        return qs.Count == 0 ? _options.HubUrl : _options.HubUrl + "?" + string.Join("&", qs);
    }

    private void WireUpHandlers(HubConnection connection)
    {
        connection.On<InvokeEnvelope, InvokeResponse>("HandleInvoke", HandleInvoke);
        connection.On<string, string, int, string>("ServeDuplex", ServeDuplex);
        connection.On<string, string>("DuplexUpstream", HandleDuplexUpstream);
        connection.On<string>("DuplexComplete", HandleDuplexComplete);
        connection.On<string>("DuplexCancel", HandleDuplexCancel);
    }

    private Task<InvokeResponse> HandleInvoke(InvokeEnvelope envelope) =>
        HandleInvokeCore(envelope, CancellationToken.None);

    private async Task<InvokeResponse> HandleInvokeCore(InvokeEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var dispatcher = dispatchers.GetInvoke(envelope.ContractId, envelope.MethodId);
            if (dispatcher is null)
                return new InvokeResponse(false, null,
                    new RpcErrorInfo("RouteNotFound",
                        $"Agent has no invoke handler for {envelope.ContractId}#{envelope.MethodId}.", null, null));

            var resultJson = await dispatcher(envelope.PayloadJson, cancellationToken).ConfigureAwait(false);
            return new InvokeResponse(true, resultJson, null);
        }
        catch (RpcException rex)
        {
            return new InvokeResponse(false, null,
                new RpcErrorInfo(rex.Kind, rex.Message, rex.RemoteTypeName, rex.Detail));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent invoke handler failed");
            return new InvokeResponse(false, null,
                new RpcErrorInfo("RemoteException", ex.Message, ex.GetType().FullName, ex.ToString()));
        }
    }

    private async Task ServeDuplex(string correlationId, string contractId, int methodId, string initialJson)
    {
        var dispatcher = dispatchers.GetDuplex(contractId, methodId);
        if (dispatcher is null)
        {
            await TryFailDuplex(correlationId,
                new RpcErrorInfo("RouteNotFound",
                    $"Agent has no duplex handler for {contractId}#{methodId}.", null, null));
            return;
        }

        var upstream = Channel.CreateUnbounded<string>();
        var downstream = Channel.CreateUnbounded<string>();
        var cts = new CancellationTokenSource();
        var session = new AgentDuplexSession(upstream, cts);
        _agentSessions.Add(correlationId, session);

        var pumpDown = Task.Run(async () =>
        {
            try
            {
                await foreach (var json in downstream.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                    await Connection.InvokeAsync("SubmitDuplexResponse", correlationId, json, cts.Token).ConfigureAwait(false);
                await Connection.InvokeAsync("CompleteDuplexResponse", correlationId, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                await TryFailDuplex(correlationId, new RpcErrorInfo(
                    "RemoteException", ex.Message, ex.GetType().FullName, ex.ToString()));
            }
        }, cts.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                await dispatcher(initialJson, upstream.Reader, downstream.Writer, cts.Token).ConfigureAwait(false);
                downstream.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                downstream.Writer.TryComplete(ex);
            }
            finally
            {
                _agentSessions.Remove(correlationId);
            }
        }, cts.Token);
    }

    private async Task HandleDuplexUpstream(string correlationId, string payloadJson)
    {
        if (_agentSessions.TryGet(correlationId, out var session))
            await session.Upstream.Writer.WriteAsync(payloadJson, session.Cts.Token).ConfigureAwait(false);
    }

    private Task HandleDuplexComplete(string correlationId)
    {
        if (_agentSessions.TryGet(correlationId, out var session))
            session.Upstream.Writer.TryComplete();
        return Task.CompletedTask;
    }

    private Task HandleDuplexCancel(string correlationId)
    {
        _agentSessions.Remove(correlationId);
        return Task.CompletedTask;
    }

    private async Task TryFailDuplex(string correlationId, RpcErrorInfo error)
    {
        try
        {
            var json = JsonSerializer.Serialize(error, RpcJson.Options);
            await Connection.InvokeAsync("FailDuplexResponse", correlationId, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to report duplex failure for correlation {CorrelationId}", correlationId);
        }
    }
}
