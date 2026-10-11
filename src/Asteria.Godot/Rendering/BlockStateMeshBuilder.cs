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
        return BuildInternal(block, blocks, fluids, textures, materials, null, null);
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
        return BuildInternal(block, blocks, fluids, textures, materials,
            layers, selectedLayer);
    }

    private static ArrayMesh BuildInternal(
        BlockStateSnapshot block,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials,
        AttachedLayerRegistry? layers,
        AttachedBlockLayer? selectedLayer)
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

        if (selectedLayer is not null)
            chunk.SetSurfaceState(0, 0, 0, block.SurfaceState);

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
                meshletIndex: 0,
                attachedLayers: layers,
                selectedLayer: selectedLayer);

        return ChunkMeshBuilder.CreateMesh(
            data,
            materials);
    }
}
