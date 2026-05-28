namespace Rpc.Contracts;

public interface IDuplexCall<TUp, TDown> : IAsyncDisposable
{
    IAsyncEnumerable<TDown> Responses { get; }

    ValueTask SendAsync(TUp message, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync();
}
