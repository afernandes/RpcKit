using Rpc.Contracts;

namespace Sample.Contracts;

[RpcContract]
public interface IPrinterService
{
    [RpcMethod(1)]
    Task<PrintResult> PrintAsync(PrintJob job, CancellationToken cancellationToken);
}

public sealed record PrintJob(string Title, string Body, int Copies = 1);

public sealed record PrintResult(bool Success, string? PrinterName, string? Error);
