namespace Rpc.Core;

public abstract record RpcAddress
{
    public static readonly RpcAddress Server = new ServerAddress();

    public static RpcAddress Device(string deviceId) => new DeviceAddress(deviceId);

    public static RpcAddress Role(Capability capability, string tenantId) =>
        new RoleAddress(capability, tenantId);

    public static RpcAddress User(string userId, Capability capability) =>
        new UserAddress(userId, capability);
}

public sealed record ServerAddress : RpcAddress;

public sealed record DeviceAddress(string DeviceId) : RpcAddress;

public sealed record RoleAddress(Capability Capability, string TenantId) : RpcAddress;

public sealed record UserAddress(string UserId, Capability Capability) : RpcAddress;
