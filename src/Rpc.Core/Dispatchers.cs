using System.Threading.Channels;

namespace Rpc.Core;

public delegate Task<string?> InvokeDispatcher(
    string payloadJson,
    CancellationToken cancellationToken);

public delegate IAsyncEnumerable<string> StreamDispatcher(
    string payloadJson,
    CancellationToken cancellationToken);

public delegate Task DuplexDispatcher(
    string initialPayloadJson,
    ChannelReader<string> upstreamFromCaller,
    ChannelWriter<string> downstreamToCaller,
    CancellationToken cancellationToken);

public interface IDispatcherRegistry
{
    void RegisterInvoke(string contractId, int methodId, InvokeDispatcher dispatcher);
    void RegisterStream(string contractId, int methodId, StreamDispatcher dispatcher);
    void RegisterDuplex(string contractId, int methodId, DuplexDispatcher dispatcher);

    InvokeDispatcher? GetInvoke(string contractId, int methodId);
    StreamDispatcher? GetStream(string contractId, int methodId);
    DuplexDispatcher? GetDuplex(string contractId, int methodId);
}

public sealed class DispatcherRegistry : IDispatcherRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), InvokeDispatcher> _invokes = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), StreamDispatcher> _streams = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), DuplexDispatcher> _duplexes = new();

    public void RegisterInvoke(string contractId, int methodId, InvokeDispatcher dispatcher) =>
        _invokes[(contractId, methodId)] = dispatcher;

    public void RegisterStream(string contractId, int methodId, StreamDispatcher dispatcher) =>
        _streams[(contractId, methodId)] = dispatcher;

    public void RegisterDuplex(string contractId, int methodId, DuplexDispatcher dispatcher) =>
        _duplexes[(contractId, methodId)] = dispatcher;

    public InvokeDispatcher? GetInvoke(string contractId, int methodId) =>
        _invokes.TryGetValue((contractId, methodId), out var d) ? d : null;

    public StreamDispatcher? GetStream(string contractId, int methodId) =>
        _streams.TryGetValue((contractId, methodId), out var d) ? d : null;

    public DuplexDispatcher? GetDuplex(string contractId, int methodId) =>
        _duplexes.TryGetValue((contractId, methodId), out var d) ? d : null;
}
