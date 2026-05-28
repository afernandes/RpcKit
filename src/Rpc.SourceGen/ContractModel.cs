using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rpc.SourceGen;

internal enum MethodKind
{
    /// <summary>Returns Task&lt;T&gt; or ValueTask&lt;T&gt;.</summary>
    InvokeResult,
    /// <summary>Returns Task or ValueTask (no result).</summary>
    InvokeVoid,
    /// <summary>Returns IAsyncEnumerable&lt;T&gt; (server-to-client stream).</summary>
    Stream,
    /// <summary>Returns IDuplexCall&lt;TUp, TDown&gt;.</summary>
    Duplex,
}

internal sealed record ParamModel(
    string Name,
    string FullTypeName);

internal sealed record MethodModel(
    string Name,
    int MethodId,
    bool Exclusive,
    MethodKind Kind,
    string ReturnTypeRendered,           // e.g. "global::System.Threading.Tasks.Task<int>"
    string? ResultElementTypeFullName,    // T in Task<T> / IAsyncEnumerable<T>; null for InvokeVoid
    string? DuplexUpFullName,
    string? DuplexDownFullName,
    bool HasCancellationToken,
    string CancellationTokenParamName,
    IReadOnlyList<ParamModel> ValueParameters);

internal sealed record ContractModel(
    string Namespace,
    string InterfaceShortName,           // "IOrderService"
    string InterfaceFullName,            // "global::Sample.Contracts.IOrderService"
    IReadOnlyList<MethodModel> Methods)
{
    public string ProxyClassName => InterfaceShortName.StartsWith("I", System.StringComparison.Ordinal)
        ? InterfaceShortName.Substring(1) + "Proxy"
        : InterfaceShortName + "Proxy";

    public string DispatcherClassName => InterfaceShortName.StartsWith("I", System.StringComparison.Ordinal)
        ? InterfaceShortName.Substring(1) + "Dispatcher"
        : InterfaceShortName + "Dispatcher";

    public static ContractModel? Build(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken cancellationToken)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol iface) return null;
        if (iface.TypeKind != TypeKind.Interface) return null;

        var ns = iface.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : iface.ContainingNamespace.ToDisplayString();
        var shortName = iface.Name;
        var fullName = "global::" + iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);

        var methods = new List<MethodModel>();
        foreach (var member in iface.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol m) continue;
            if (m.MethodKind != Microsoft.CodeAnalysis.MethodKind.Ordinary) continue;

            var methodModel = BuildMethod(m);
            if (methodModel is null) continue;
            methods.Add(methodModel);
        }

        if (methods.Count == 0) return null;
        return new ContractModel(ns, shortName, fullName, methods);
    }

    private static MethodModel? BuildMethod(IMethodSymbol m)
    {
        // [RpcMethod(id, Exclusive=...)] — recover Id and Exclusive
        var rpcMethodAttr = m.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "Rpc.Contracts.RpcMethodAttribute");
        if (rpcMethodAttr is null) return null;

        var methodId = 0;
        if (rpcMethodAttr.ConstructorArguments.Length > 0 &&
            rpcMethodAttr.ConstructorArguments[0].Value is int idValue)
        {
            methodId = idValue;
        }

        var exclusive = false;
        foreach (var named in rpcMethodAttr.NamedArguments)
        {
            if (named.Key == "Exclusive" && named.Value.Value is bool b) exclusive = b;
        }

        // classify return type
        var rt = m.ReturnType;
        var (kind, resultT, dupUp, dupDown, rendered) = ClassifyReturn(rt);
        if (kind is null) return null; // unsupported return type

        // collect parameters (filter CancellationToken)
        var paramList = new List<ParamModel>();
        string? ctParamName = null;
        foreach (var p in m.Parameters)
        {
            var fullType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fullType == "global::System.Threading.CancellationToken")
            {
                ctParamName = p.Name;
                continue;
            }
            paramList.Add(new ParamModel(p.Name, fullType));
        }

        return new MethodModel(
            Name: m.Name,
            MethodId: methodId,
            Exclusive: exclusive,
            Kind: kind.Value,
            ReturnTypeRendered: rendered!,
            ResultElementTypeFullName: resultT,
            DuplexUpFullName: dupUp,
            DuplexDownFullName: dupDown,
            HasCancellationToken: ctParamName != null,
            CancellationTokenParamName: ctParamName ?? "cancellationToken",
            ValueParameters: paramList);
    }

    private static (MethodKind? Kind, string? ResultT, string? DupUp, string? DupDown, string? Rendered)
        ClassifyReturn(ITypeSymbol rt)
    {
        var fullName = rt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rendered = fullName;

        // Task<T> / ValueTask<T>
        if (rt is INamedTypeSymbol named && named.IsGenericType)
        {
            var ctor = named.ConstructedFrom.ToDisplayString();
            switch (ctor)
            {
                case "System.Threading.Tasks.Task<TResult>":
                case "System.Threading.Tasks.ValueTask<TResult>":
                {
                    var t = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return (MethodKind.InvokeResult, t, null, null, rendered);
                }
                case "System.Collections.Generic.IAsyncEnumerable<T>":
                {
                    var t = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return (MethodKind.Stream, t, null, null, rendered);
                }
                case "Rpc.Contracts.IDuplexCall<TUp, TDown>":
                {
                    var up = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var down = named.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return (MethodKind.Duplex, null, up, down, rendered);
                }
            }
        }

        if (fullName == "global::System.Threading.Tasks.Task" ||
            fullName == "global::System.Threading.Tasks.ValueTask")
        {
            return (MethodKind.InvokeVoid, null, null, null, rendered);
        }

        return (null, null, null, null, null);
    }
}
