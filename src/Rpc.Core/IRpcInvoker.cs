using Rpc.Contracts;

namespace Rpc.Core;

public interface IRpcInvoker
{
    Task<string?> InvokeAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> OpenStreamAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default);

    Task<IDuplexCall<string, string>> OpenDuplexAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default);
}
