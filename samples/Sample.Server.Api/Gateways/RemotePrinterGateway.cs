using Rpc.Core;
using Sample.Contracts;
using Sample.Contracts.Gateways;

namespace Sample.Server.Api.Gateways;

public sealed class RemotePrinterGateway(IRpcClient rpc) : IPrinterGateway
{
    public Task<PrintResult> PrintAsync(string capability, string tenantId, PrintJob job, CancellationToken cancellationToken)
    {
        var printer = rpc.For<IPrinterService>(
            RpcAddress.Role(new Capability(capability), tenantId));
        return printer.PrintAsync(job, cancellationToken);
    }
}
