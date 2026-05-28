using Sample.Contracts;

namespace Sample.Server.Api.Implementations;

public sealed class OrderServiceImpl : IOrderService
{
    public Task<OpenOrdersResult> GetOpenOrdersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new OpenOrdersResult(new List<OrderSummary>
        {
            new("ORD-001", "Mesa 5", 87.50m),
            new("ORD-002", "Mesa 12", 154.30m),
            new("ORD-003", "Balcão", 22.00m),
        }));
}
