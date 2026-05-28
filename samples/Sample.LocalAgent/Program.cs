using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rpc.Core;
using Rpc.SignalR.Client;
using Sample.Contracts;
using Sample.Contracts.Generated;
using Sample.LocalAgent.Implementations;

var builder = Host.CreateApplicationBuilder(args);

// Roda como systemd unit no Linux quando rodado sob systemd. No-op em outros casos.
builder.Services.AddSystemd();

builder.Services.AddSingleton<ITefService, TefServiceImpl>();
builder.Services.AddSingleton<PrinterServiceImpl>();
builder.Services.AddSingleton<IPrinterService>(sp => sp.GetRequiredService<PrinterServiceImpl>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrinterServiceImpl>());
builder.Services.AddSingleton<IPosTerminal, PosTerminalImpl>();

var rpc = builder.Services.AddRpcClient(o =>
{
    o.HubUrl = builder.Configuration["Rpc:HubUrl"] ?? "http://localhost:5000/rpc";
})
.UseStaticIdentity(new RpcIdentity(
    DeviceId: builder.Configuration["Rpc:DeviceId"] ?? Environment.MachineName + ":agent",
    UserId: builder.Configuration["Rpc:UserId"],
    TenantId: builder.Configuration["Rpc:TenantId"],
    Capabilities: [Capability.Tef, Capability.PrinterKitchen, Capability.PosTerminal]));

builder.Services.AddSingleton<IHostedService>(sp =>
{
    // Extensões geradas pelo Rpc.SourceGen — pegam o impl via DI automaticamente.
    sp.GetRequiredService<IDispatcherRegistry>()
        .AddTefServiceDispatcher(sp)
        .AddPrinterServiceDispatcher(sp)
        .AddPosTerminalDispatcher(sp);
    return new NoopHostedService();
});

await builder.Build().RunAsync();

internal sealed class NoopHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
