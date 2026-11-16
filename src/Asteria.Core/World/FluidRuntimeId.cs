namespace Asteria.Core.World;

public readonly record struct FluidRuntimeId(ushort Value)
{
    public static FluidRuntimeId None => default;

    public bool IsNone => Value == 0;

    public override string ToString() => Value.ToString();
}
