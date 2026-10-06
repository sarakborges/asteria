using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public static class BlockStateMeshBuilder
{
    public static ArrayMesh Build(
        BlockStateSnapshot block,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(materials);

        var chunk = new Chunk();
        chunk.SetCell(
            0,
            0,
            0,
            block.Cell);

        if (block.HasMicroblockGeometry)
        {
            chunk.SetMicroblockMask(
                0,
                0,
                0,
                block.MicroblockMask);
        }

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            ChunkMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                textures,
                meshletIndex: 0);

        return ChunkMeshBuilder.CreateMesh(
            data,
            materials);
    }
}
