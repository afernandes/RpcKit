using System.Text.Json;
using Rpc.Contracts;

namespace Rpc.Core;

public sealed class DeferredDuplexCall<TUp, TDown>(
    Task<IDuplexCall<string, string>> openTask,
    JsonSerializerOptions? options = null) : IDuplexCall<TUp, TDown>
{
    private readonly JsonSerializerOptions _options = options ?? RpcJson.Options;
    private IDuplexCall<string, string>? _inner;

    public IAsyncEnumerable<TDown> Responses => Iterate();

    private async IAsyncEnumerable<TDown> Iterate()
    {
        var inner = await EnsureAsync().ConfigureAwait(false);
        await foreach (var json in inner.Responses.ConfigureAwait(false))
        {
            var value = JsonSerializer.Deserialize<TDown>(json, _options);
            if (value is not null) yield return value;
        }
    }

    public async ValueTask SendAsync(TUp message, CancellationToken cancellationToken = default)
    {
        var inner = await EnsureAsync().ConfigureAwait(false);
        var json = JsonSerializer.Serialize(message, _options);
        await inner.SendAsync(json, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteAsync()
    {
        var inner = await EnsureAsync().ConfigureAwait(false);
        await inner.CompleteAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_inner is not null)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            return;
        }
        try { _inner = await openTask.ConfigureAwait(false); } catch { return; }
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<IDuplexCall<string, string>> EnsureAsync()
    {
        _inner ??= await openTask.ConfigureAwait(false);
        return _inner;
    }
}
