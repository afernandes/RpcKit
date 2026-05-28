namespace Rpc.SignalR.Server;

public sealed class RpcServerOptions
{
    public TimeSpan ClientInvokeTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
