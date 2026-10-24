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
        return ChunkMeshBuilder.CreateMesh(
            BuildData(block, blocks, fluids, textures), materials);
    }

    /// <summary>
    /// Constructs a decal exclusively from the selected top layer, reusing
    /// terrain fine-face coverage, rotation and authored stacking offsets.
    /// </summary>
    public static ArrayMesh BuildAttachedLayer(
        BlockStateSnapshot block,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials,
        AttachedLayerRegistry layers,
        AttachedBlockLayer selectedLayer)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (!block.SurfaceState.TryGetTop(selectedLayer.Face, out var top) ||
            top != selectedLayer)
            throw new ArgumentException(
                "Crack layer must match the top authored attachment.",
                nameof(selectedLayer));
        return ChunkMeshBuilder.CreateMesh(
            BuildDataInternal(block, blocks, fluids, textures,
                layers, selectedLayer), materials);
    }

    /// <summary>Actual terrain geometry reused for preview icon pixels.</summary>
    public static ChunkMeshData BuildData(
        BlockStateSnapshot block, BlockRegistry blocks,
        FluidRegistry fluids, TerrainTextureLookup textures,
        AttachedLayerRegistry? layers = null, DyeRegistry? dyes = null) =>
        BuildDataInternal(block, blocks, fluids, textures, layers, null, dyes);

    private static ChunkMeshData BuildDataInternal(
        BlockStateSnapshot block,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        AttachedLayerRegistry? layers,
        AttachedBlockLayer? selectedLayer, DyeRegistry? dyes = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(textures);
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

        if (layers is not null)
            chunk.SetSurfaceState(0, 0, 0, block.SurfaceState);

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
                meshletIndex: 0,
                attachedLayers: layers,
                dyes: dyes,
                selectedLayer: selectedLayer);

        return data;
    }
}
