using Rpc.Contracts;
using Rpc.Core;
using Sample.Contracts;
using Sample.Contracts.Gateways;

namespace Sample.Server.Api.Gateways;

/// <summary>
/// RemoteTefGateway uses RpcKit to route TEF calls to the agent running on the user's machine.
/// Used by Blazor Server (where the C# runs on the server, separate from the pinpad host).
/// </summary>
public sealed class RemoteTefGateway(IRpcClient rpc, ICurrentUser user) : ITefGateway
{
    public IDuplexCall<TefResponse, TefEvent> Charge(TefRequest initial, CancellationToken cancellationToken)
    {
        var tef = rpc.For<ITefService>(RpcAddress.User(user.UserId, Capability.Tef));
        return tef.Charge(initial, cancellationToken);
    }
}

public interface ICurrentUser
{
    string UserId { get; }
}

/// <summary>Demo stand-in. Production: read from auth context (HttpContext.User claims).</summary>
public sealed class DemoCurrentUser(string userId) : ICurrentUser
{
    public string UserId { get; } = userId;
}
