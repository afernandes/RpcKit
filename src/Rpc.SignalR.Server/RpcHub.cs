using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Rpc.Contracts;
using Rpc.Core;

namespace Rpc.SignalR.Server;

public sealed class RpcHub(
    IDeviceRegistry devices,
    IRpcRouter router,
    PendingDuplexRegistry pendingDuplexes,
    ExclusiveCallRegistry exclusiveCalls,
    ILogger<RpcHub> logger) : Hub
{
    public const string Path = "/rpc";
    public const string ClientHandleInvoke = "HandleInvoke";
    public const string ClientServeDuplex = "ServeDuplex";
    public const string ClientDuplexUpstream = "DuplexUpstream";
    public const string ClientDuplexComplete = "DuplexComplete";
    public const string ClientDuplexCancel = "DuplexCancel";

    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var query = http?.Request.Query;

        var deviceId = query?["deviceId"].ToString();
        if (string.IsNullOrWhiteSpace(deviceId)) deviceId = Context.ConnectionId;

        var tenantId = query?["tenantId"].ToString();
        if (string.IsNullOrWhiteSpace(tenantId)) tenantId = null;

        var userId = query?["userId"].ToString();
        if (string.IsNullOrWhiteSpace(userId)) userId = null;

        var caps = (query?["caps"].ToString() ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        devices.Register(new DeviceEntry(Context.ConnectionId, deviceId, tenantId, userId, caps));
        logger.LogInformation("RPC client connected: device={DeviceId} caps=[{Caps}] tenant={Tenant} user={User}",
            deviceId, string.Join(",", caps), tenantId, userId);

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        pendingDuplexes.CancelAllForConnection(Context.ConnectionId);
        exclusiveCalls.ReleaseAllForConnection(Context.ConnectionId);
        devices.Unregister(Context.ConnectionId);
        logger.LogInformation("RPC client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task<InvokeResponse> Invoke(InvokeEnvelope envelope)
    {
        var ct = Context.ConnectionAborted;
        try
        {
            var resultJson = await router.InvokeAsync(envelope, ct).ConfigureAwait(false);
            return new InvokeResponse(true, resultJson, null);
        }
        catch (RpcException rex)
        {
            return new InvokeResponse(false, null,
                new RpcErrorInfo(rex.Kind, rex.Message, rex.RemoteTypeName, rex.Detail));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invoke failed for {Contract}#{Method}", envelope.ContractId, envelope.MethodId);
            return new InvokeResponse(false, null,
                new RpcErrorInfo("RemoteException", ex.Message, ex.GetType().FullName, ex.ToString()));
        }
    }

    public IAsyncEnumerable<string> OpenStream(
        InvokeEnvelope envelope,
        CancellationToken cancellationToken)
        => router.OpenStreamAsync(envelope, cancellationToken);

    public async IAsyncEnumerable<string> OpenDuplex(
        InvokeEnvelope envelope,
        ChannelReader<string> upstream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (envelope.Target.Kind == nameof(ServerAddress))
        {
            var downstream = Channel.CreateUnbounded<string>();
            var task = Task.Run(() =>
                router.DispatchServerDuplexAsync(envelope, upstream, downstream.Writer, cancellationToken),
                cancellationToken);
            try
            {
                await foreach (var json in downstream.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    yield return json;
            }
            finally
            {
                try { await task.ConfigureAwait(false); } catch { /* surfaced via channel completion */ }
            }
            yield break;
        }

        IDuplexCall<string, string>? remote = null;
        RpcException? openError = null;
        try
        {
            remote = await router.OpenRemoteDuplexAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException rex)
        {
            openError = rex;
        }

        if (openError is not null)
        {
            yield return RpcWireProtocol.FormatErrorFrame(
                new RpcErrorInfo(openError.Kind, openError.Message, openError.RemoteTypeName, openError.Detail));
            yield break;
        }

        var live = remote!;
        await using (live)
        {
            var pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (var json in upstream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                        await live.SendAsync(json, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* normal */ }
                finally { await live.CompleteAsync().ConfigureAwait(false); }
            }, cancellationToken);

            try
            {
                await foreach (var json in live.Responses.WithCancellation(cancellationToken).ConfigureAwait(false))
                    yield return json;
            }
            finally
            {
                try { await pump.ConfigureAwait(false); } catch { /* swallow */ }
            }
        }
    }

    public Task SubmitDuplexResponse(string correlationId, string payloadJson)
        => router.SubmitDuplexResponseAsync(correlationId, payloadJson, Context.ConnectionAborted);

    public Task CompleteDuplexResponse(string correlationId)
        => router.CompleteDuplexResponseAsync(correlationId);

    public Task FailDuplexResponse(string correlationId, string errorJson)
        => router.FailDuplexResponseAsync(correlationId, errorJson);
}
