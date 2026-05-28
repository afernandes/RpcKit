using Microsoft.Extensions.DependencyInjection;
using Rpc.Core;

namespace Rpc.SignalR.Client;

public sealed class RpcClientBuilder(
    IServiceCollection services,
    ProxyRegistry proxies,
    DispatcherRegistry dispatchers)
{
    /// <summary>Acesso ao <see cref="IServiceCollection"/> pra extension methods registrarem serviços.</summary>
    public IServiceCollection Services => services;

    public ProxyRegistry Proxies => proxies;
    public IDispatcherRegistry Dispatchers => dispatchers;

    public RpcClientBuilder AddProxy<T>(ProxyFactory factory) where T : class
    {
        proxies.Register(typeof(T), factory);
        return this;
    }

    public RpcClientBuilder AddInvokeHandler(string contractId, int methodId, InvokeDispatcher dispatcher)
    {
        dispatchers.RegisterInvoke(contractId, methodId, dispatcher);
        return this;
    }

    public RpcClientBuilder AddStreamHandler(string contractId, int methodId, StreamDispatcher dispatcher)
    {
        dispatchers.RegisterStream(contractId, methodId, dispatcher);
        return this;
    }

    public RpcClientBuilder AddDuplexHandler(string contractId, int methodId, DuplexDispatcher dispatcher)
    {
        dispatchers.RegisterDuplex(contractId, methodId, dispatcher);
        return this;
    }

    // -- Identity providers -----------------------------------------------

    /// <summary>
    /// Identidade fixa do agente — para worker service / kiosk / dev. Atalho que registra
    /// um <see cref="StaticRpcIdentityProvider"/> com o <paramref name="identity"/> fornecido.
    /// </summary>
    public RpcClientBuilder UseStaticIdentity(RpcIdentity identity)
    {
        services.AddSingleton<IRpcIdentityProvider>(new StaticRpcIdentityProvider(identity));
        return this;
    }

    /// <summary>
    /// Registra um <see cref="IRpcIdentityProvider"/> custom — para apps com login, onde
    /// a identidade precisa ser resolvida em runtime (ex: ler sessão atual).
    /// </summary>
    public RpcClientBuilder UseIdentityProvider<TProvider>() where TProvider : class, IRpcIdentityProvider
    {
        services.AddSingleton<IRpcIdentityProvider, TProvider>();
        return this;
    }

    public RpcClientBuilder UseIdentityProvider(Func<IServiceProvider, IRpcIdentityProvider> factory)
    {
        services.AddSingleton(factory);
        return this;
    }
}
