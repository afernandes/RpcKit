using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Rpc.SignalR.Client;

internal sealed class AgentDuplexSession(
    Channel<string> upstream,
    CancellationTokenSource cts)
{
    public Channel<string> Upstream { get; } = upstream;
    public CancellationTokenSource Cts { get; } = cts;
}

internal sealed class AgentDuplexSessions
{
    private readonly ConcurrentDictionary<string, AgentDuplexSession> _sessions = new();

    public void Add(string correlationId, AgentDuplexSession session) =>
        _sessions[correlationId] = session;

    public bool TryGet(string correlationId, out AgentDuplexSession session)
    {
        if (_sessions.TryGetValue(correlationId, out var s))
        {
            session = s;
            return true;
        }
        session = null!;
        return false;
    }

    public void Remove(string correlationId)
    {
        if (_sessions.TryRemove(correlationId, out var s))
        {
            try { s.Cts.Cancel(); } catch { }
            s.Upstream.Writer.TryComplete();
        }
    }
}
