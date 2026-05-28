// Polyfill for C# 9+ records on netstandard2.0 (which Roslyn analyzers target).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
