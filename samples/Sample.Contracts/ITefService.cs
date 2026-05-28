using System.Text.Json.Serialization;
using Rpc.Contracts;

namespace Sample.Contracts;

[RpcContract]
public interface ITefService
{
    [RpcMethod(1, Exclusive = true)]
    IDuplexCall<TefResponse, TefEvent> Charge(TefRequest initial, CancellationToken cancellationToken);
}

public sealed record TefRequest(decimal Amount, string OperatorId, string MerchantReference);

public sealed record TefResponse(string Text);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TefDisplayMessage), nameof(TefDisplayMessage))]
[JsonDerivedType(typeof(TefCollectInput), nameof(TefCollectInput))]
[JsonDerivedType(typeof(TefMenu), nameof(TefMenu))]
[JsonDerivedType(typeof(TefConfirm), nameof(TefConfirm))]
[JsonDerivedType(typeof(TefCompleted), nameof(TefCompleted))]
[JsonDerivedType(typeof(TefError), nameof(TefError))]
public abstract record TefEvent;

public sealed record TefDisplayMessage(string Text) : TefEvent;

public sealed record TefCollectInput(string Prompt, int MinLength, int MaxLength) : TefEvent;

public sealed record TefMenu(string Title, IReadOnlyList<string> Options) : TefEvent;

public sealed record TefConfirm(string Prompt) : TefEvent;

public sealed record TefCompleted(string Nsu, string AuthorizationCode, string Receipt) : TefEvent;

public sealed record TefError(int Code, string Message) : TefEvent;
