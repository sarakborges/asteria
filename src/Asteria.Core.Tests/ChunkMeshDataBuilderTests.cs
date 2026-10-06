using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkMeshDataBuilderTests
{
    [Fact]
    public void SingleOpaqueCubeBuildsTerrainAndCollisionInCore()
    {
        var texture =
            new BlockTextureLayer("res://textures/test.png");
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone",
                    textures:
                        new BlockTextureSet(
                            top: [texture])),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, stone);

        var fluids = new FluidRegistry([]);
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
                new TerrainTextureLookup(
                    new Dictionary<string, int>
                    {
                        ["res://textures/test.png"] = 0,
                    }),
                meshletIndex: 0);

        Assert.True(data.HasGeometry);
        Assert.Equal(12, data.TriangleCount);
        Assert.Equal(12, data.CollisionTriangleCount);
    }

    [Fact]
    public void FluidGeometryBuildsWithoutGodotTypes()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(79, 159, 214),
                    opacity: 0.72f),
            ]);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        chunk.SetFluid(
            1,
            1,
            1,
            FluidCell.Source(water));
        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                fluids,
                meshletIndex: 0);

        Assert.True(data.HasGeometry);
        Assert.True(data.TriangleCount > 0);
    }
}
