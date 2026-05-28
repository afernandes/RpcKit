namespace Rpc.Core;

public interface IRpcClient
{
    T For<T>(RpcAddress address) where T : class;
}
