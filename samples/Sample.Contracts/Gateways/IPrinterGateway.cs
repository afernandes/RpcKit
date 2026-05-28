namespace Sample.Contracts.Gateways;

/// <summary>
/// Application-level port for printing. Two implementations parallel to <see cref="ITefGateway"/>:
/// direct (ESC/POS on a local serial/USB port) and remote (RPC via the printer agent).
/// </summary>
public interface IPrinterGateway
{
    Task<PrintResult> PrintAsync(string capability, string tenantId, PrintJob job, CancellationToken cancellationToken);
}
