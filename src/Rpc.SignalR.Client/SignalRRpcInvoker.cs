using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Rpc.Contracts;
using Rpc.Core;

namespace Rpc.SignalR.Client;

public sealed class SignalRRpcInvoker(HubConnection connection) : IRpcInvoker
{
    public const string InvokeMethod = "Invoke";
    public const string OpenStreamMethod = "OpenStream";
    public const string OpenDuplexMethod = "OpenDuplex";

    public async Task<string?> InvokeAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default)
    {
        var envelope = NewEnvelope(address, contractId, methodId, payloadJson, exclusive);
        var response = await connection.InvokeAsync<InvokeResponse>(
            InvokeMethod, envelope, cancellationToken).ConfigureAwait(false);
        if (!response.Success && response.Error is not null)
            throw RpcException.FromError(response.Error);
        return response.PayloadJson;
    }

    public IAsyncEnumerable<string> OpenStreamAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default)
    {
        var envelope = NewEnvelope(address, contractId, methodId, payloadJson, exclusive);
        return connection.StreamAsync<string>(OpenStreamMethod, envelope, cancellationToken);
    }

    public Task<IDuplexCall<string, string>> OpenDuplexAsync(
        RpcAddress address,
        string contractId,
        int methodId,
        string payloadJson,
        bool exclusive = false,
        CancellationToken cancellationToken = default)
    {
        var envelope = NewEnvelope(address, contractId, methodId, payloadJson, exclusive);
        var upstream = Channel.CreateUnbounded<string>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var downstream = connection.StreamAsync<string>(
            OpenDuplexMethod, envelope, upstream.Reader, cts.Token);
        IDuplexCall<string, string> call = new SignalRDuplexCall(upstream, downstream, cts);
        return Task.FromResult(call);
    }

    private static InvokeEnvelope NewEnvelope(
        RpcAddress address, string contractId, int methodId, string payloadJson, bool exclusive) =>
        new(contractId, methodId, AddressEnvelope.From(address), null, null, payloadJson, exclusive);
}
