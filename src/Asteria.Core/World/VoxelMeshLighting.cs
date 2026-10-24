namespace Asteria.Core.World;

public readonly record struct VoxelVertexLighting(
    float Sky,
    float BlockRed,
    float BlockGreen,
    float BlockBlue,
    float AmbientOcclusion);

public readonly record struct VoxelFaceLighting(
    VoxelVertexLighting Corner0,
    VoxelVertexLighting Corner1,
    VoxelVertexLighting Corner2,
    VoxelVertexLighting Corner3)
{
    public VoxelVertexLighting this[int index] => index switch
    {
        0 => Corner0,
        1 => Corner1,
        2 => Corner2,
        3 => Corner3,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public bool ShouldFlipDiagonal => false;
}

/// <summary>Returns neutral vertex attributes; does not query or propagate light.</summary>
public static class VoxelMeshLighting
{
    private static readonly VoxelVertexLighting NeutralVertex =
        new(1f, 0f, 0f, 0f, 1f);

    private static readonly VoxelFaceLighting NeutralFace =
        new(NeutralVertex, NeutralVertex, NeutralVertex, NeutralVertex);

    public static VoxelFaceLighting SampleFace(
        Chunk chunk, BlockRegistry blocks, int x, int y, int z, BlockFace face) =>
        NeutralFace;

    public static VoxelFaceLighting SampleFace(
        VoxelWorld world, BlockRegistry blocks, WorldVoxelCoord position, BlockFace face) =>
        NeutralFace;

    public sealed class SamplingGrid
    {
        private SamplingGrid() { }

        public static SamplingGrid Capture(
            VoxelWorld world, BlockRegistry blocks, ChunkCoord coord, int meshletIndex) =>
            new();

        public VoxelFaceLighting SampleFace(WorldVoxelCoord position, BlockFace face) =>
            NeutralFace;
    }
}
