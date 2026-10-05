namespace Asteria.Core.World;

public readonly record struct VoxelCell
{
    public VoxelCell(BlockRuntimeId block, byte orientation = 0, ushort state = 0)
    {
        if (block.IsAir)
        {
            Block = BlockRuntimeId.Air;
            Orientation = 0;
            State = 0;
            return;
        }

        Block = block;
        Orientation = orientation;
        State = state;
    }

    public BlockRuntimeId Block { get; }

    public byte Orientation { get; }

    public ushort State { get; }

    public bool IsEmpty => Block.IsAir;

    public static VoxelCell Empty => default;
}
