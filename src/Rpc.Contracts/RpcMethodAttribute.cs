namespace Rpc.Contracts;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RpcMethodAttribute(int id) : Attribute
{
    public int Id { get; } = id;

    /// <summary>
    /// When true, the router rejects a new invocation while a previous one targeting the same
    /// connection + contract + method is still in flight, with <c>RpcException(Kind = "Busy")</c>.
    /// Use for resources that physically allow only one operation at a time, like a TEF pinpad.
    /// Do NOT use for resources that benefit from queueing (printers) — handle that in the impl.
    /// </summary>
    public bool Exclusive { get; init; }
}
