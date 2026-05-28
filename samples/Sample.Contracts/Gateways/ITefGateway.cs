using Rpc.Contracts;

namespace Sample.Contracts.Gateways;

/// <summary>
/// Application-level port for TEF. Components depend on this — NOT on <see cref="ITefService"/>.
/// <para>
/// Two implementations:
/// <list type="bullet">
/// <item><b>DirectTefGateway</b> — used in MAUI/Self-Checkout where the app is on the same machine
/// as the pinpad. Calls CliSiTef bindings directly. No RPC.</item>
/// <item><b>RemoteTefGateway</b> — used in Blazor Server (or any multi-process scenario). Wraps
/// <see cref="ITefService"/> via <c>IRpcClient</c> and <see cref="Rpc.Core.UserAddress"/>.</item>
/// </list>
/// </para>
/// </summary>
public interface ITefGateway
{
    IDuplexCall<TefResponse, TefEvent> Charge(TefRequest initial, CancellationToken cancellationToken);
}
