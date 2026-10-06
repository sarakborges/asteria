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

        Assert.True(data.TriangleCount > 0);
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

    [Fact]
    public void IsolatedFluidTopUsesAuthoredCellHeight()
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
            FluidCell.Spreading(
                water,
                FluidCell.MinLevel,
                spreadDistance: 7));

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
        var topVertices =
            Assert.Single(data.Batches)
                .Vertices
                .Where(vertex =>
                    vertex.Normal ==
                    System.Numerics.Vector3.UnitY)
                .ToArray();

        Assert.Equal(6, topVertices.Length);

        foreach (var vertex in topVertices)
        {
            Assert.Equal(
                1f + FluidCell.MinLevel / 8f,
                vertex.Position.Y,
                precision: 5);
        }
    }

    [Fact]
    public void FluidTopSurfaceHasDedicatedInteriorFacingTriangles()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        79,
                        159,
                        214),
                    opacity: 0.72f),
            ]);
        var water =
            fluids.GetId(
                "asteria:water");
        var chunk =
            new Chunk();

        chunk.SetFluid(
            1,
            1,
            1,
            FluidCell.Source(water));

        var world =
            new VoxelWorld();
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
        var vertices =
            Assert.Single(
                data.Batches)
                .Vertices;
        var topHeight =
            2f;

        var exterior =
            vertices.Count(vertex =>
                vertex.Normal ==
                    System.Numerics.Vector3.UnitY &&
                MathF.Abs(
                    vertex.Position.Y -
                    topHeight) <
                0.0001f);
        var interior =
            vertices.Count(vertex =>
                vertex.Normal ==
                    -System.Numerics.Vector3.UnitY &&
                MathF.Abs(
                    vertex.Position.Y -
                    topHeight) <
                0.0001f);

        Assert.Equal(6, exterior);
        Assert.Equal(6, interior);
    }

    [Fact]
    public void FluidSurfaceHeightMatchesAcrossChunkSeam()
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
        var left = new Chunk();
        var right = new Chunk();

        left.SetFluid(
            Chunk.Size - 1,
            1,
            8,
            FluidCell.Spreading(
                water,
                FluidCell.MaxLevel,
                spreadDistance: 0));
        right.SetFluid(
            0,
            1,
            8,
            FluidCell.Spreading(
                water,
                FluidCell.MinLevel,
                spreadDistance: 7));

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            left);
        world.InsertChunk(
            new ChunkCoord(1, 0, 0),
            right);

        var leftData =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                fluids,
                meshletIndex: 3);
        var rightData =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                new ChunkCoord(1, 0, 0),
                blocks,
                fluids,
                meshletIndex: 2);

        var leftEdgeHeights =
            Assert.Single(leftData.Batches)
                .Vertices
                .Where(vertex =>
                    vertex.Normal ==
                        System.Numerics.Vector3.UnitY &&
                    MathF.Abs(
                        vertex.Position.X -
                        Chunk.Size) <
                    0.0001f)
                .Select(vertex => vertex.Position.Y)
                .Distinct()
                .Order()
                .ToArray();
        var rightEdgeHeights =
            Assert.Single(rightData.Batches)
                .Vertices
                .Where(vertex =>
                    vertex.Normal ==
                        System.Numerics.Vector3.UnitY &&
                    MathF.Abs(
                        vertex.Position.X) <
                    0.0001f)
                .Select(vertex => vertex.Position.Y)
                .Distinct()
                .Order()
                .ToArray();

        Assert.NotEmpty(leftEdgeHeights);
        Assert.Equal(
            leftEdgeHeights,
            rightEdgeHeights);
    }
}
