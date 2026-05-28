using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.Contracts;
using Rpc.Core;

namespace Rpc.SignalR.Server;

public sealed class RpcRouter(
    IHubContext<RpcHub> hubContext,
    IDeviceRegistry devices,
    IDispatcherRegistry dispatchers,
    PendingDuplexRegistry pendingDuplexes,
    ExclusiveCallRegistry exclusiveCalls,
    IOptions<RpcServerOptions> options,
    ILogger<RpcRouter> logger) : IRpcRouter
{
    private readonly RpcServerOptions _options = options.Value;

    public async Task<string?> InvokeAsync(InvokeEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Target.Kind == nameof(ServerAddress))
        {
            var dispatcher = dispatchers.GetInvoke(envelope.ContractId, envelope.MethodId)
                ?? throw new RpcRouteNotFoundException(
                    $"No server-side invoke dispatcher for {envelope.ContractId}#{envelope.MethodId}.");

            var serverCorr = envelope.Exclusive ? AcquireOrThrow(
                ExclusiveCallRegistry.ServerConnectionToken, envelope) : null;
            try
            {
                return await dispatcher(envelope.PayloadJson, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (serverCorr is not null)
                    exclusiveCalls.Release(
                        ExclusiveCallRegistry.ServerConnectionToken, envelope.ContractId, envelope.MethodId);
            }
        }

        var target = ResolveTarget(envelope) ?? throw new RpcRouteNotFoundException(
            $"No device found for {envelope.Target.Kind}.");

        var remoteCorr = envelope.Exclusive ? AcquireOrThrow(target.ConnectionId, envelope) : null;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_options.ClientInvokeTimeout);

            var response = await hubContext.Clients.Client(target.ConnectionId)
                .InvokeAsync<InvokeResponse>(RpcHub.ClientHandleInvoke, envelope, linked.Token).ConfigureAwait(false);
            if (!response.Success && response.Error is not null)
                throw RpcException.FromError(response.Error);
            return response.PayloadJson;
        }
        finally
        {
            if (remoteCorr is not null)
                exclusiveCalls.Release(target.ConnectionId, envelope.ContractId, envelope.MethodId);
        }
    }

    public async IAsyncEnumerable<string> OpenStreamAsync(
        InvokeEnvelope envelope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (envelope.Target.Kind != nameof(ServerAddress))
            throw new NotSupportedException("Streaming to remote clients is not yet supported.");

        var dispatcher = dispatchers.GetStream(envelope.ContractId, envelope.MethodId)
            ?? throw new RpcRouteNotFoundException(
                $"No server-side stream dispatcher for {envelope.ContractId}#{envelope.MethodId}.");

        var corr = envelope.Exclusive ? AcquireOrThrow(
            ExclusiveCallRegistry.ServerConnectionToken, envelope) : null;
        try
        {
            await foreach (var json in dispatcher(envelope.PayloadJson, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return json;
            }
        }
        finally
        {
            if (corr is not null)
                exclusiveCalls.Release(
                    ExclusiveCallRegistry.ServerConnectionToken, envelope.ContractId, envelope.MethodId);
        }
    }

    public async Task DispatchServerDuplexAsync(
        InvokeEnvelope envelope,
        ChannelReader<string> upstream,
        ChannelWriter<string> downstream,
        CancellationToken cancellationToken)
    {
        var dispatcher = dispatchers.GetDuplex(envelope.ContractId, envelope.MethodId)
            ?? throw new RpcRouteNotFoundException(
                $"No server-side duplex dispatcher for {envelope.ContractId}#{envelope.MethodId}.");

        var corr = envelope.Exclusive ? AcquireOrThrow(
            ExclusiveCallRegistry.ServerConnectionToken, envelope) : null;
        try
        {
            await dispatcher(envelope.PayloadJson, upstream, downstream, cancellationToken).ConfigureAwait(false);
            downstream.TryComplete();
        }
        catch (Exception ex)
        {
            downstream.TryComplete(ex);
            throw;
        }
        finally
        {
            if (corr is not null)
                exclusiveCalls.Release(
                    ExclusiveCallRegistry.ServerConnectionToken, envelope.ContractId, envelope.MethodId);
        }
    }

    public async Task<IDuplexCall<string, string>> OpenRemoteDuplexAsync(
        InvokeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var target = ResolveTarget(envelope) ?? throw new RpcRouteNotFoundException(
            $"No device found for {envelope.Target.Kind}.");

        var correlationId = Guid.NewGuid().ToString("N");

        if (envelope.Exclusive && !exclusiveCalls.TryAcquire(
                target.ConnectionId, envelope.ContractId, envelope.MethodId, correlationId))
        {
            throw new RpcBusyException(
                $"Target connection is already processing an exclusive call for {envelope.ContractId}#{envelope.MethodId}.");
        }

        var session = new PendingDuplexSession { ServingConnectionId = target.ConnectionId };
        pendingDuplexes.Register(correlationId, session);

        var upstream = Channel.CreateUnbounded<string>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await hubContext.Clients.Client(target.ConnectionId).SendAsync(
            RpcHub.ClientServeDuplex,
            correlationId,
            envelope.ContractId,
            envelope.MethodId,
            envelope.PayloadJson,
            cts.Token).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var json in upstream.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                {
                    await hubContext.Clients.Client(target.ConnectionId)
                        .SendAsync(RpcHub.ClientDuplexUpstream, correlationId, json, cts.Token).ConfigureAwait(false);
                }
                await hubContext.Clients.Client(target.ConnectionId)
                    .SendAsync(RpcHub.ClientDuplexComplete, correlationId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Remote duplex upstream pump failed for {Correlation}", correlationId);
            }
        }, cts.Token);

        return new RemoteDuplexCall(
            upstream,
            session.Downstream.Reader,
            cts,
            () => SafeCancelRemote(target.ConnectionId, correlationId),
            () =>
            {
                pendingDuplexes.Remove(correlationId);
                if (envelope.Exclusive)
                    exclusiveCalls.Release(target.ConnectionId, envelope.ContractId, envelope.MethodId);
            });
    }

    public Task SubmitDuplexResponseAsync(string correlationId, string payloadJson, CancellationToken cancellationToken)
    {
        var session = pendingDuplexes.Get(correlationId);
        return session is null
            ? Task.CompletedTask
            : session.Downstream.Writer.WriteAsync(payloadJson, cancellationToken).AsTask();
    }

    public Task CompleteDuplexResponseAsync(string correlationId)
    {
        var session = pendingDuplexes.Get(correlationId);
        session?.Downstream.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public Task FailDuplexResponseAsync(string correlationId, string errorJson)
    {
        var session = pendingDuplexes.Get(correlationId);
        if (session is null) return Task.CompletedTask;

        var err = JsonSerializer.Deserialize<RpcErrorInfo>(errorJson, RpcJson.Options)
            ?? new RpcErrorInfo("RemoteException", "Agent reported an unknown error.", null, null);
        session.Downstream.Writer.TryComplete(RpcException.FromError(err));
        return Task.CompletedTask;
    }

    private string AcquireOrThrow(string connectionId, InvokeEnvelope envelope)
    {
        var corr = envelope.CorrelationId ?? Guid.NewGuid().ToString("N");
        if (!exclusiveCalls.TryAcquire(connectionId, envelope.ContractId, envelope.MethodId, corr))
        {
            throw new RpcBusyException(
                connectionId == ExclusiveCallRegistry.ServerConnectionToken
                    ? $"Server is already processing an exclusive call for {envelope.ContractId}#{envelope.MethodId}."
                    : $"Target connection is already processing an exclusive call for {envelope.ContractId}#{envelope.MethodId}.");
        }
        return corr;
    }

    private async Task SafeCancelRemote(string connectionId, string correlationId)
    {
        try
        {
            await hubContext.Clients.Client(connectionId)
                .SendAsync(RpcHub.ClientDuplexCancel, correlationId, CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* agent already gone */ }
    }

    private DeviceEntry? ResolveTarget(InvokeEnvelope envelope) => envelope.Target.Kind switch
    {
        nameof(DeviceAddress) => devices.ResolveByDeviceId(envelope.Target.DeviceId!),
        nameof(RoleAddress) => devices.ResolveByCapability(
            new Capability(envelope.Target.Capability!), envelope.Target.TenantId),
        nameof(UserAddress) => devices.ResolveByUserCapability(
            envelope.Target.UserId!, new Capability(envelope.Target.Capability!)),
        _ => null
    };
}

internal sealed class RemoteDuplexCall(
    Channel<string> upstream,
    ChannelReader<string> downstream,
    CancellationTokenSource cts,
    Func<Task> cancelRemoteFn,
    Action removeSession) : IDuplexCall<string, string>
{
    private int _disposed;

    public IAsyncEnumerable<string> Responses => downstream.ReadAllAsync(cts.Token);

    public ValueTask SendAsync(string message, CancellationToken cancellationToken = default)
        => upstream.Writer.WriteAsync(message, cancellationToken);

    public ValueTask CompleteAsync()
    {
        upstream.Writer.TryComplete();
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { cts.Cancel(); } catch { }
        upstream.Writer.TryComplete();
        try { await cancelRemoteFn().ConfigureAwait(false); } catch { }
        removeSession();
        cts.Dispose();
    }
}
