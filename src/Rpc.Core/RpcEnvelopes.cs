namespace Rpc.Core;

public sealed record InvokeEnvelope(
    string ContractId,
    int MethodId,
    AddressEnvelope Target,
    string? TenantId,
    string? CorrelationId,
    string PayloadJson,
    bool Exclusive = false);

public sealed record InvokeResponse(
    bool Success,
    string? PayloadJson,
    RpcErrorInfo? Error);

public sealed record RpcErrorInfo(
    string Kind,
    string Message,
    string? TypeName,
    string? Detail);

public sealed record AddressEnvelope(
    string Kind,
    string? DeviceId,
    string? Capability,
    string? TenantId,
    string? UserId)
{
    public static AddressEnvelope From(RpcAddress address) => address switch
    {
        ServerAddress => new(nameof(ServerAddress), null, null, null, null),
        DeviceAddress d => new(nameof(DeviceAddress), d.DeviceId, null, null, null),
        RoleAddress r => new(nameof(RoleAddress), null, r.Capability.Name, r.TenantId, null),
        UserAddress u => new(nameof(UserAddress), null, u.Capability.Name, null, u.UserId),
        _ => throw new ArgumentOutOfRangeException(nameof(address))
    };

    public RpcAddress ToAddress() => Kind switch
    {
        nameof(ServerAddress) => RpcAddress.Server,
        nameof(DeviceAddress) => RpcAddress.Device(DeviceId!),
        nameof(RoleAddress) => RpcAddress.Role(new Capability(Capability!), TenantId!),
        nameof(UserAddress) => RpcAddress.User(UserId!, new Capability(Capability!)),
        _ => throw new InvalidOperationException($"Unknown address kind '{Kind}'.")
    };
}
