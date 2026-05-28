using System.Text.Json;
using Rpc.Contracts;

namespace Rpc.Core;

public sealed class JsonDuplexAdapter<TUp, TDown>(
    IDuplexCall<string, string> inner,
    JsonSerializerOptions? options = null) : IDuplexCall<TUp, TDown>
{
    private readonly JsonSerializerOptions _options = options ?? RpcJson.Options;

    public IAsyncEnumerable<TDown> Responses => IterateResponses();

    private async IAsyncEnumerable<TDown> IterateResponses()
    {
        await foreach (var json in inner.Responses.ConfigureAwait(false))
        {
            var value = JsonSerializer.Deserialize<TDown>(json, _options);
            if (value is not null) yield return value;
        }
    }

    public ValueTask SendAsync(TUp message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, _options);
        return inner.SendAsync(json, cancellationToken);
    }

    public ValueTask CompleteAsync() => inner.CompleteAsync();

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
