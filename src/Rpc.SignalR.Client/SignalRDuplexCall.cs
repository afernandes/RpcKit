using System.Threading.Channels;
using Rpc.Contracts;
using Rpc.Core;

namespace Rpc.SignalR.Client;

internal sealed class SignalRDuplexCall(
    Channel<string> upstream,
    IAsyncEnumerable<string> downstream,
    CancellationTokenSource cts) : IDuplexCall<string, string>
{
    public IAsyncEnumerable<string> Responses => IterateAsync();

    private async IAsyncEnumerable<string> IterateAsync()
    {
        await foreach (var chunk in downstream.ConfigureAwait(false))
        {
            if (RpcWireProtocol.TryParseErrorFrame(chunk, out var info))
                throw RpcException.FromError(info!);
            yield return chunk;
        }
    }

    public ValueTask SendAsync(string message, CancellationToken cancellationToken = default)
        => upstream.Writer.WriteAsync(message, cancellationToken);

    public ValueTask CompleteAsync()
    {
        upstream.Writer.TryComplete();
        return default;
    }

    public ValueTask DisposeAsync()
    {
        try { cts.Cancel(); } catch { /* swallow */ }
        upstream.Writer.TryComplete();
        cts.Dispose();
        return default;
    }
}
