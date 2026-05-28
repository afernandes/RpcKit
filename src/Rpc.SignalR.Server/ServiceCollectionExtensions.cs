using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Rpc.Core;

namespace Rpc.SignalR.Server;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRpcServer(
        this IServiceCollection services,
        Action<RpcServerOptions>? configure = null)
    {
        services.AddSignalR(o => o.MaximumReceiveMessageSize = null);
        services.Configure(configure ?? (_ => { }));
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IDispatcherRegistry, DispatcherRegistry>();
        services.AddSingleton<PendingDuplexRegistry>();
        services.AddSingleton<ExclusiveCallRegistry>();
        services.AddSingleton<IRpcRouter, RpcRouter>();
        services.AddSingleton<IRpcInvoker, InProcessRpcInvoker>();
        services.AddSingleton<ProxyRegistry>();
        services.AddSingleton<IRpcClient>(sp =>
            new RpcClient(sp.GetRequiredService<IRpcInvoker>(), sp.GetRequiredService<ProxyRegistry>()));
        return services;
    }

    public static HubEndpointConventionBuilder MapRpcHub(this IEndpointRouteBuilder endpoints, string? pattern = null)
        => endpoints.MapHub<RpcHub>(pattern ?? RpcHub.Path);
}
