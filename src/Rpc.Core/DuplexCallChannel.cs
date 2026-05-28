using System.Threading.Channels;
using Rpc.Contracts;

namespace Rpc.Core;

public sealed class DuplexCallChannel<TUp, TDown> : IDuplexCall<TUp, TDown>
{
    private readonly Channel<TUp> _up = Channel.CreateUnbounded<TUp>();
    private readonly Channel<TDown> _down = Channel.CreateUnbounded<TDown>();
    private readonly CancellationTokenSource _cts;
    private int _disposed;

    public DuplexCallChannel(CancellationToken linkToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(linkToken);
    }

    public CancellationToken Token => _cts.Token;
    public ChannelWriter<TDown> Emit => _down.Writer;
    public ChannelReader<TUp> Received => _up.Reader;

    public IAsyncEnumerable<TDown> Responses => _down.Reader.ReadAllAsync(_cts.Token);

    public ValueTask SendAsync(TUp message, CancellationToken cancellationToken = default)
        => _up.Writer.WriteAsync(message, cancellationToken);

    public ValueTask CompleteAsync()
    {
        _up.Writer.TryComplete();
        return default;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return default;
        try { _cts.Cancel(); } catch { /* swallow */ }
        _up.Writer.TryComplete();
        _down.Writer.TryComplete();
        _cts.Dispose();
        return default;
    }
}
