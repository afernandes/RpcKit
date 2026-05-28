using System.Text.Json;

namespace Rpc.Core;

/// <summary>
/// Wire-level conventions shared between SignalR server and client.
/// </summary>
public static class RpcWireProtocol
{
    /// <summary>
    /// Streaming methods (OpenStream / OpenDuplex) can't return an <see cref="InvokeResponse"/>
    /// like single-call <c>Invoke</c> does, and SignalR wraps server-side exceptions in
    /// <c>HubException</c> with a flattened message. To preserve the typed <see cref="RpcException"/>
    /// across the wire, the server emits a single string frame prefixed with this marker and
    /// followed by a JSON-encoded <see cref="RpcErrorInfo"/>. The client recognises the prefix
    /// and re-throws the typed exception.
    /// </summary>
    public const string ErrorFramePrefix = "__rpc_err__:";

    public static string FormatErrorFrame(RpcErrorInfo info) =>
        ErrorFramePrefix + JsonSerializer.Serialize(info, RpcJson.Options);

    public static bool TryParseErrorFrame(string chunk, out RpcErrorInfo? info)
    {
        info = null;
        if (!chunk.StartsWith(ErrorFramePrefix, StringComparison.Ordinal)) return false;
        var json = chunk[ErrorFramePrefix.Length..];
        info = JsonSerializer.Deserialize<RpcErrorInfo>(json, RpcJson.Options);
        return true;
    }
}
