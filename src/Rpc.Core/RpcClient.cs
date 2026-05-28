namespace Rpc.Core;

public sealed class RpcClient(IRpcInvoker invoker, ProxyRegistry proxies) : IRpcClient
{
    public T For<T>(RpcAddress address) where T : class
    {
        var factory = proxies.Get(typeof(T))
            ?? throw new InvalidOperationException(
                $"No proxy registered for {typeof(T).FullName}. Call AddProxy<{typeof(T).Name}>(...) during RpcClient setup.");
        return (T)factory(invoker, address);
    }
}
