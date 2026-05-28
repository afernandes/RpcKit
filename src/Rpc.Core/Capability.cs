namespace Rpc.Core;

public readonly record struct Capability(string Name)
{
    public static readonly Capability Tef = new("tef");
    public static readonly Capability PrinterKitchen = new("printer.kitchen");
    public static readonly Capability PrinterPdv = new("printer.pdv");
    public static readonly Capability PosTerminal = new("pos.terminal");

    public override string ToString() => Name;
}
