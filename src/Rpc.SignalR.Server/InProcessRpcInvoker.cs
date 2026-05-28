using Rpc.Contracts;
using Rpc.Core;

namespace Rpc.SignalR.Server;

public sealed class InProcessRpcInvoker(IRpcRouter router) : IRpcInvoker
{
    public Task<string?> InvokeAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default)
    {
        var envelope = NewEnvelope(address, contractId, methodId, payloadJson, exclusive);
        return router.InvokeAsync(envelope, cancellationToken);
    }

    public IAsyncEnumerable<string> OpenStreamAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default)
    {
        var envelope = NewEnvelope(address, contractId, methodId, payloadJson, exclusive);
        return router.OpenStreamAsync(envelope, cancellationToken);
    }

    public Task<IDuplexCall<string, string>> OpenDuplexAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default)
    {
        var envelope = NewEnvelope(address, contractId, methodId, payloadJson, exclusive);
        return address is ServerAddress
            ? OpenServerDuplexInProcess(envelope, cancellationToken)
            : router.OpenRemoteDuplexAsync(envelope, cancellationToken);
    }

    private async Task<IDuplexCall<string, string>> OpenServerDuplexInProcess(
        InvokeEnvelope envelope, CancellationToken cancellationToken)
    {
        var upstream = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var downstream = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await router.DispatchServerDuplexAsync(envelope, upstream.Reader, downstream.Writer, cts.Token)
                    .ConfigureAwait(false);
                downstream.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                downstream.Writer.TryComplete(ex);
            }
        }, cts.Token);

        IDuplexCall<string, string> call = new InProcessDuplex(upstream, downstream.Reader, cts);
        return await Task.FromResult(call).ConfigureAwait(false);
    }

    private static InvokeEnvelope NewEnvelope(
        RpcAddress address, string contractId, int methodId, string payloadJson, bool exclusive) =>
        new(contractId, methodId, AddressEnvelope.From(address), null, null, payloadJson, exclusive);

    private sealed class InProcessDuplex(
        System.Threading.Channels.Channel<string> upstream,
        System.Threading.Channels.ChannelReader<string> downstream,
        CancellationTokenSource cts) : IDuplexCall<string, string>
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

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return default;
            try { cts.Cancel(); } catch { }
            upstream.Writer.TryComplete();
            cts.Dispose();
            return default;
        }
    }
}
