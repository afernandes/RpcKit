using System.Threading.Channels;
using Rpc.Contracts;
using Rpc.Core;

namespace Rpc.SignalR.Server;

public interface IRpcRouter
{
    Task<string?> InvokeAsync(InvokeEnvelope envelope, CancellationToken cancellationToken);

    IAsyncEnumerable<string> OpenStreamAsync(InvokeEnvelope envelope, CancellationToken cancellationToken);

    Task DispatchServerDuplexAsync(
        InvokeEnvelope envelope,
        ChannelReader<string> upstream,
        ChannelWriter<string> downstream,
        CancellationToken cancellationToken);

    Task<IDuplexCall<string, string>> OpenRemoteDuplexAsync(
        InvokeEnvelope envelope,
        CancellationToken cancellationToken);

    Task SubmitDuplexResponseAsync(string correlationId, string payloadJson, CancellationToken cancellationToken);

    Task CompleteDuplexResponseAsync(string correlationId);

    Task FailDuplexResponseAsync(string correlationId, string errorJson);
}
