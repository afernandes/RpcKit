using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rpc.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class RpcContractGenerator : IIncrementalGenerator
{
    private const string RpcContractAttributeFullName = "Rpc.Contracts.RpcContractAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RpcContractAttributeFullName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ContractModel.Build(ctx, ct))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        // Per-contract emission: proxy + dispatcher + extensions.
        context.RegisterSourceOutput(contracts, (spc, contract) =>
        {
            var source = ContractEmitter.Emit(contract);
            spc.AddSource(
                hintName: $"{contract.InterfaceShortName}.RpcGenerated.g.cs",
                sourceText: SourceText.From(source, System.Text.Encoding.UTF8));
        });

        // Per-assembly emission. Combina com AssemblyName pra que o nome da classe agregada
        // seja único quando vários assemblies de contratos coexistem no consumidor.
        var collected = contracts.Collect().Combine(context.CompilationProvider);
        context.RegisterSourceOutput(collected, (spc, tuple) =>
        {
            var (contracts, compilation) = tuple;
            if (contracts.Length == 0) return;
            var assemblyName = compilation.AssemblyName ?? "Unknown";
            var source = AssemblyExtensionsEmitter.Emit(contracts.ToList(), assemblyName);
            spc.AddSource(
                hintName: "__RpcGeneratedExtensions.g.cs",
                sourceText: SourceText.From(source, System.Text.Encoding.UTF8));
        });
    }
}
