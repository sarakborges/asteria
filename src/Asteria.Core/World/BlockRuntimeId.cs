namespace Asteria.Core.World;

public readonly record struct BlockRuntimeId(ushort Value)
{
    public static BlockRuntimeId Air => default;

    public bool IsAir => Value == 0;

    public override string ToString() => Value.ToString();
}
