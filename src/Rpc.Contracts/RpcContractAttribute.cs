namespace Rpc.Contracts;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class RpcContractAttribute : Attribute
{
    public string? Name { get; init; }
}
