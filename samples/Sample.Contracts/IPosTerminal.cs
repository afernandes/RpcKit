using Rpc.Contracts;

namespace Sample.Contracts;

[RpcContract]
public interface IPosTerminal
{
    [RpcMethod(1)]
    Task<PingResult> PingAsync(CancellationToken cancellationToken);
}

public sealed record PingResult(string DeviceId, DateTimeOffset Timestamp, string Status);
