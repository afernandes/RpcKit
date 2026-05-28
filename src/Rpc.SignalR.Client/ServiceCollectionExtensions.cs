using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rpc.Core;

namespace Rpc.SignalR.Client;

public static class ServiceCollectionExtensions
{
    public static RpcClientBuilder AddRpcClient(
        this IServiceCollection services,
        Action<RpcClientOptions> configure)
    {
        services.Configure(configure);

        var proxies = new ProxyRegistry();
        var dispatchers = new DispatcherRegistry();
        services.AddSingleton(proxies);
        services.AddSingleton(dispatchers);
        services.AddSingleton<IDispatcherRegistry>(sp => sp.GetRequiredService<DispatcherRegistry>());

        services.AddSingleton<RpcConnectionHost>();
        services.AddHostedService(sp => sp.GetRequiredService<RpcConnectionHost>());

        services.AddSingleton<IRpcInvoker>(sp =>
            new SignalRRpcInvoker(sp.GetRequiredService<RpcConnectionHost>().Connection));
        services.AddSingleton<IRpcClient>(sp =>
            new RpcClient(sp.GetRequiredService<IRpcInvoker>(), proxies));

        return new RpcClientBuilder(services, proxies, dispatchers);
    }
}
