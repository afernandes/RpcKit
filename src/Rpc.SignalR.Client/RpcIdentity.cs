using Rpc.Core;

namespace Rpc.SignalR.Client;

/// <summary>
/// Identidade que o agente declara no handshake do Hub. Combinada — todos os campos
/// vêm na MESMA leitura (tipicamente de uma sessão única), garantindo consistência:
/// não dá pra ter o <see cref="UserId"/> de um login e o <see cref="TenantId"/> de outro.
/// </summary>
public sealed record RpcIdentity(
    string DeviceId,
    string? UserId = null,
    string? TenantId = null,
    IReadOnlyList<Capability>? Capabilities = null)
{
    public IReadOnlyList<Capability> CapabilitiesOrEmpty => Capabilities ?? System.Array.Empty<Capability>();

    /// <summary>Conveniência para casos sem usuário/tenant — só deviceId + capabilities.</summary>
    public static RpcIdentity ForDevice(string deviceId, params Capability[] capabilities) =>
        new(deviceId, Capabilities: capabilities);
}

/// <summary>
/// Resolve a identidade do agente para uma (re)conexão. Chamada uma vez por tentativa de
/// conexão — incluindo após <see cref="RpcConnectionHost.ReconnectAsync"/> disparado por
/// login/logout. Implemente esta interface em vez de configurar 4 callbacks separados.
/// </summary>
public interface IRpcIdentityProvider
{
    Task<RpcIdentity> GetIdentityAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Identidade fixa — para worker services rodando como service account, kiosks sem login,
/// agentes de teste, e cenários onde a identidade do agente não muda durante a vida do processo.
/// </summary>
public sealed class StaticRpcIdentityProvider(RpcIdentity identity) : IRpcIdentityProvider
{
    public Task<RpcIdentity> GetIdentityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(identity);
}
