using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class AttachedLayerRenderingTests
{
    private const string StonePath = "textures/blocks/stone.png";
    private const string LayerPath = "textures/blocks/grass/side_overlay.png";

    private static (VoxelWorld World, BlockRegistry Blocks, AttachedLayerRegistry Layers) Setup(
        bool sculpted = false)
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone",
                textures: new BlockTextureSet(top: [new BlockTextureLayer(StonePath)]),
                tags: ["fragmentable"])
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, blocks.GetId("asteria:stone"));
        chunk.SetSurfaceState(1, 1, 1, new BlockSurfaceState(null, [
            new AttachedBlockLayer(BlockFace.Top, "asteria:foliage_layer")
        ]));
        if (sculpted)
        {
            var mask = MicroblockMask.Full.Edit(
                0, 0, 0, MicroblockResolution.Thin, occupied: false);
            chunk.SetMicroblockMask(1, 1, 1, mask);
        }
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:foliage_layer","texture":"textures/blocks/grass/side_overlay.png",
             "tint":"foliage","faces":["top"],"offset":0.002,"alphaCutoff":0.5}
            """
        ]);
        return (world, blocks, layers);
    }

    private static ChunkMeshData Mesh(bool sculpted = false)
    {
        var (world, blocks, layers) = Setup(sculpted);
        return ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                [StonePath] = 1, [LayerPath] = 2
            }), 0, attachedLayers: layers);
    }

    [Fact]
    public void AttachedLayerAddsCutoutQuadsWithoutCollision()
    {
        var data = Mesh();
        Assert.Equal(14, data.TriangleCount);
        Assert.Equal(12, data.CollisionTriangleCount);
        var overlay = Assert.Single(data.RenderBatches.Where(
            batch => batch.Batch.RenderMode == BlockRenderMode.Cutout));
        Assert.Equal(2, overlay.TriangleCount);
        Assert.All(overlay.Vertices, vertex =>
        {
            Assert.Equal(2.25f, vertex.EncodedTextureLayers.X);
            Assert.True(vertex.Position.Y > 2f);
        });
    }

    [Fact]
    public void SculptedHostProjectsLayerOntoExposedMicrofaces()
    {
        var data = Mesh(sculpted: true);
        Assert.Contains(data.RenderBatches, batch =>
            batch.Batch.RenderMode == BlockRenderMode.Cutout &&
            batch.TriangleCount > 0);
        Assert.Equal(
            data.RenderBatches.Where(batch => batch.Batch.RenderMode == BlockRenderMode.Opaque)
                .Sum(batch => batch.TriangleCount),
            data.CollisionTriangleCount);
    }


    [Fact]
    public void IndependentLayersCanCoverWallFacesWithoutOccupyingAdjacentVoxels()
    {
        var stone = new BlockDefinition("asteria:stone",
            textures: new BlockTextureSet(
                front: [new BlockTextureLayer(StonePath)]));
        var blocks = new BlockRegistry([stone]);
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, blocks.GetId("asteria:stone"));
        chunk.SetSurfaceState(1, 1, 1, new BlockSurfaceState(null, [
            new AttachedBlockLayer(BlockFace.Front, "asteria:wall_moss"),
            new AttachedBlockLayer(BlockFace.Front, "asteria:wall_lichen")
        ]));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var layerDefs = new AttachedLayerRegistry([
            new AttachedLayerDefinition("asteria:wall_moss", LayerPath,
                faces: [BlockFace.Front]),
            new AttachedLayerDefinition("asteria:wall_lichen", LayerPath,
                faces: [BlockFace.Front])
        ]);

        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                [StonePath] = 1, [LayerPath] = 2
            }), 0, attachedLayers: layerDefs);

        Assert.Equal(1, chunk.NonEmptyVoxelCount);
        Assert.True(chunk.GetCell(1, 1, 2).IsEmpty);
        Assert.Equal(12, mesh.CollisionTriangleCount);
        var overlay = Assert.Single(mesh.RenderBatches.Where(batch =>
            batch.Batch.RenderMode == BlockRenderMode.Cutout));
        Assert.Equal(4, overlay.TriangleCount);
        Assert.Equal(2, chunk.GetSurfaceState(1, 1, 1).Layers.Count);
    }

    [Fact]
    public void MissingDefinitionFailsInsteadOfSilentlyDiscardingWorldData()
    {
        var (world, blocks, _) = Setup();
        Assert.Throws<KeyNotFoundException>(() =>
            ChunkMeshDataBuilder.BuildMeshlet(
                world, ChunkCoord.Zero, blocks,
                new TerrainTextureLookup(new Dictionary<string, int> { [StonePath] = 1 }),
                0, attachedLayers: new AttachedLayerRegistry([])));
    }
}
