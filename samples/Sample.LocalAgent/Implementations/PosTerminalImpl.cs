using Sample.Contracts;

namespace Sample.LocalAgent.Implementations;

public sealed class PosTerminalImpl : IPosTerminal
{
    public Task<PingResult> PingAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PingResult(Environment.MachineName, DateTimeOffset.UtcNow, "ok"));
}
