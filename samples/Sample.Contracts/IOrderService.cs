using Rpc.Contracts;

namespace Sample.Contracts;

[RpcContract]
public interface IOrderService
{
    [RpcMethod(1)]
    Task<OpenOrdersResult> GetOpenOrdersAsync(CancellationToken cancellationToken);
}

public sealed record OpenOrdersResult(IReadOnlyList<OrderSummary> Orders);

public sealed record OrderSummary(string OrderId, string TableName, decimal Total);
