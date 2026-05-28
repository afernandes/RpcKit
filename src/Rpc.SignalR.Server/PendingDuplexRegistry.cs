using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Rpc.SignalR.Server;

public sealed class PendingDuplexSession
{
    public Channel<string> Downstream { get; } = Channel.CreateUnbounded<string>();
    public string ServingConnectionId { get; init; } = string.Empty;
    public CancellationTokenSource Cts { get; } = new();
}

public sealed class PendingDuplexRegistry
{
    private readonly ConcurrentDictionary<string, PendingDuplexSession> _byCorrelation = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _byConnection = new();
    private readonly object _connLock = new();

    public void Register(string correlationId, PendingDuplexSession session)
    {
        _byCorrelation[correlationId] = session;
        lock (_connLock)
        {
            if (!_byConnection.TryGetValue(session.ServingConnectionId, out var set))
                _byConnection[session.ServingConnectionId] = set = new HashSet<string>();
            set.Add(correlationId);
        }
    }

    public PendingDuplexSession? Get(string correlationId) =>
        _byCorrelation.TryGetValue(correlationId, out var s) ? s : null;

    public void Remove(string correlationId)
    {
        if (_byCorrelation.TryRemove(correlationId, out var s))
        {
            lock (_connLock)
            {
                if (_byConnection.TryGetValue(s.ServingConnectionId, out var set))
                    set.Remove(correlationId);
            }
            s.Downstream.Writer.TryComplete();
            try { s.Cts.Cancel(); } catch { /* swallow */ }
            s.Cts.Dispose();
        }
    }

    public void CancelAllForConnection(string connectionId)
    {
        string[] ids;
        lock (_connLock)
        {
            if (!_byConnection.TryGetValue(connectionId, out var set)) return;
            ids = set.ToArray();
        }
        foreach (var id in ids) Remove(id);
    }
}
