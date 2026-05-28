namespace Rpc.SignalR.Client;

public sealed class RpcClientOptions
{
    public string HubUrl { get; set; } = "http://localhost:5000/rpc";

    /// <summary>
    /// JWT provider — passado diretamente ao SignalR. SignalR chama em cada conexão e
    /// reconexão automaticamente. A identidade do agente (DeviceId/UserId/TenantId/Capabilities)
    /// é configurada via <see cref="IRpcIdentityProvider"/> registrado no DI.
    /// </summary>
    public Func<Task<string?>>? AccessTokenProvider { get; set; }

    public TimeSpan ServerKeepAlive { get; set; } = TimeSpan.FromSeconds(15);
}
