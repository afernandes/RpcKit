using System.Collections.Concurrent;

namespace Rpc.SignalR.Server;

/// <summary>
/// Tracks in-flight invocations marked <c>Exclusive = true</c> per
/// (connection, contract, method) tuple. The router consults this registry
/// to reject a second concurrent invocation targeting the same connection
/// before it reaches the agent.
/// </summary>
public sealed class ExclusiveCallRegistry
{
    public const string ServerConnectionToken = "__server__";

    private readonly ConcurrentDictionary<(string ConnectionId, string ContractId, int MethodId), string> _inFlight = new();
    private readonly ConcurrentDictionary<string, HashSet<(string, int)>> _byConnection = new();
    private readonly object _connLock = new();

    public bool TryAcquire(string connectionId, string contractId, int methodId, string correlationId)
    {
        var key = (connectionId, contractId, methodId);
        if (!_inFlight.TryAdd(key, correlationId)) return false;

        lock (_connLock)
        {
            if (!_byConnection.TryGetValue(connectionId, out var set))
                _byConnection[connectionId] = set = new HashSet<(string, int)>();
            set.Add((contractId, methodId));
        }
        return true;
    }

    public void Release(string connectionId, string contractId, int methodId)
    {
        if (_inFlight.TryRemove((connectionId, contractId, methodId), out _))
        {
            lock (_connLock)
            {
                if (_byConnection.TryGetValue(connectionId, out var set))
                    set.Remove((contractId, methodId));
            }
        }
    }

    public void ReleaseAllForConnection(string connectionId)
    {
        (string, int)[] tuples;
        lock (_connLock)
        {
            if (!_byConnection.TryRemove(connectionId, out var set)) return;
            tuples = set.ToArray();
        }
        foreach (var (contractId, methodId) in tuples)
            _inFlight.TryRemove((connectionId, contractId, methodId), out _);
    }
}
