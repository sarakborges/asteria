namespace Asteria.Core.World;

public readonly record struct VoxelCell
{
    public VoxelCell(
        BlockRuntimeId block,
        TextureRotation textureRotation = TextureRotation.Degrees0,
        BlockOrientation orientation = BlockOrientation.Y,
        HorizontalFacing facing = HorizontalFacing.South,
        ushort state = 0,
        ushort microblockMaskId = 0,
        bool isPlayerPlaced = false)
    {
        if (block.IsAir)
        {
            Block = BlockRuntimeId.Air;
            TextureRotation = TextureRotation.Degrees0;
            Orientation = BlockOrientation.Y;
            Facing = HorizontalFacing.South;
            State = 0;
            MicroblockMaskId = 0;
            IsPlayerPlaced = false;
            return;
        }

        Block = block;
        TextureRotation = textureRotation;
        Orientation = orientation;
        Facing = facing;
        State = state;
        MicroblockMaskId = microblockMaskId;
        IsPlayerPlaced = isPlayerPlaced;
    }

    public BlockRuntimeId Block { get; }

    public TextureRotation TextureRotation { get; }

    public BlockOrientation Orientation { get; }

    public HorizontalFacing Facing { get; }

    public ushort State { get; }

    public ushort MicroblockMaskId { get; }

    /// <summary>Persistent provenance that protects intentionally placed leaves from natural decay.</summary>
    public bool IsPlayerPlaced { get; }

    public bool IsEmpty => Block.IsAir;

    public bool HasMicroblockGeometry => !IsEmpty && MicroblockMaskId != 0;

    public static VoxelCell Empty => default;

    public VoxelCell WithOrientation(BlockOrientation orientation) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, orientation, Facing, State, MicroblockMaskId, IsPlayerPlaced);

    public VoxelCell WithFacing(HorizontalFacing facing) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, Orientation, facing, State, MicroblockMaskId, IsPlayerPlaced);

    public VoxelCell WithTextureRotation(TextureRotation rotation) =>
        IsEmpty ? this : new VoxelCell(Block, rotation, Orientation, Facing, State, MicroblockMaskId, IsPlayerPlaced);

    public VoxelCell WithMicroblockMaskId(ushort maskId) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, Orientation, Facing, State, maskId, IsPlayerPlaced);

    public VoxelCell WithPlayerPlaced(bool isPlayerPlaced) =>
        IsEmpty ? this : new VoxelCell(Block, TextureRotation, Orientation, Facing, State, MicroblockMaskId, isPlayerPlaced);
}
