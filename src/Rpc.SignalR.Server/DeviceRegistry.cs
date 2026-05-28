using System.Collections.Concurrent;
using Rpc.Core;

namespace Rpc.SignalR.Server;

public sealed record DeviceEntry(
    string ConnectionId,
    string DeviceId,
    string? TenantId,
    string? UserId,
    IReadOnlySet<string> Capabilities);

public interface IDeviceRegistry
{
    void Register(DeviceEntry entry);
    void Unregister(string connectionId);
    DeviceEntry? FindByConnection(string connectionId);
    DeviceEntry? ResolveByDeviceId(string deviceId);
    DeviceEntry? ResolveByCapability(Capability capability, string? tenantId);
    DeviceEntry? ResolveByUserCapability(string userId, Capability capability);
}

public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceEntry> _byConnection = new();
    private readonly ConcurrentDictionary<string, string> _deviceIdToConnection = new();

    public void Register(DeviceEntry entry)
    {
        _byConnection[entry.ConnectionId] = entry;
        _deviceIdToConnection[entry.DeviceId] = entry.ConnectionId;
    }

    public void Unregister(string connectionId)
    {
        if (_byConnection.TryRemove(connectionId, out var entry))
            _deviceIdToConnection.TryRemove(entry.DeviceId, out _);
    }

    public DeviceEntry? FindByConnection(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var entry) ? entry : null;

    public DeviceEntry? ResolveByDeviceId(string deviceId) =>
        _deviceIdToConnection.TryGetValue(deviceId, out var conn)
            && _byConnection.TryGetValue(conn, out var entry)
            ? entry
            : null;

    public DeviceEntry? ResolveByCapability(Capability capability, string? tenantId) =>
        _byConnection.Values.FirstOrDefault(e =>
            e.Capabilities.Contains(capability.Name) &&
            (tenantId is null || string.Equals(e.TenantId, tenantId, StringComparison.Ordinal)));

    public DeviceEntry? ResolveByUserCapability(string userId, Capability capability) =>
        _byConnection.Values.FirstOrDefault(e =>
            string.Equals(e.UserId, userId, StringComparison.Ordinal) &&
            e.Capabilities.Contains(capability.Name));
}
