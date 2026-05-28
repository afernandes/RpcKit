using System.Collections.Concurrent;

namespace Rpc.Core;

public delegate object ProxyFactory(IRpcInvoker invoker, RpcAddress address);

public sealed class ProxyRegistry
{
    private readonly ConcurrentDictionary<Type, ProxyFactory> _factories = new();

    public void Register(Type contractType, ProxyFactory factory) =>
        _factories[contractType] = factory;

    public ProxyFactory? Get(Type contractType) =>
        _factories.TryGetValue(contractType, out var f) ? f : null;
}
