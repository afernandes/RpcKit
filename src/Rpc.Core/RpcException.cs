namespace Rpc.Core;

public class RpcException(string kind, string message, string? typeName = null, string? detail = null)
    : Exception(message)
{
    public string Kind { get; } = kind;
    public string? RemoteTypeName { get; } = typeName;
    public string? Detail { get; } = detail;

    public static RpcException FromError(RpcErrorInfo info) => info.Kind switch
    {
        "Busy" => new RpcBusyException(info.Message),
        "RouteNotFound" => new RpcRouteNotFoundException(info.Message),
        "Timeout" => new RpcTimeoutException(info.Message),
        _ => new RpcException(info.Kind, info.Message, info.TypeName, info.Detail)
    };
}

public sealed class RpcRouteNotFoundException(string message) : RpcException("RouteNotFound", message);

public sealed class RpcTimeoutException(string message) : RpcException("Timeout", message);

public sealed class RpcBusyException(string message) : RpcException("Busy", message);
