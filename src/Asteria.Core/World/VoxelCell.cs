namespace Asteria.Core.World;

public readonly record struct VoxelCell
{
    public VoxelCell(
        BlockRuntimeId block,
        TextureRotation textureRotation = TextureRotation.Degrees0,
        BlockOrientation orientation = BlockOrientation.Y,
        HorizontalFacing facing = HorizontalFacing.South,
        ushort state = 0,
        ushort microblockMaskId = 0)
    {
        if (block.IsAir)
        {
            Block = BlockRuntimeId.Air;
            TextureRotation = TextureRotation.Degrees0;
            Orientation = BlockOrientation.Y;
            Facing = HorizontalFacing.South;
            State = 0;
            MicroblockMaskId = 0;
            return;
        }

        Block = block;
        TextureRotation = textureRotation;
        Orientation = orientation;
        Facing = facing;
        State = state;
        MicroblockMaskId = microblockMaskId;
    }

    public BlockRuntimeId Block { get; }

    public TextureRotation TextureRotation { get; }

    public BlockOrientation Orientation { get; }

    public HorizontalFacing Facing { get; }

    public ushort State { get; }

    public ushort MicroblockMaskId { get; }

    public bool IsEmpty => Block.IsAir;

    public bool HasMicroblockGeometry => !IsEmpty && MicroblockMaskId != 0;

    public static VoxelCell Empty => default;

    public VoxelCell WithOrientation(BlockOrientation orientation) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, orientation, Facing, State, MicroblockMaskId);

    public VoxelCell WithFacing(HorizontalFacing facing) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, Orientation, facing, State, MicroblockMaskId);

    public VoxelCell WithTextureRotation(TextureRotation rotation) =>
        IsEmpty ? this : new VoxelCell(Block, rotation, Orientation, Facing, State, MicroblockMaskId);

    public VoxelCell WithMicroblockMaskId(ushort maskId) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, Orientation, Facing, State, maskId);
}
