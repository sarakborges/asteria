using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidDecorationCoexistenceTests
{
    private static readonly WorldVoxelCoord WaterPosition = new(2, 2, 2);
    private static readonly WorldVoxelCoord NeighborPosition = new(3, 2, 2);

    [Fact]
    public void FluidMeshRendersSideBesideNonCollidableDecoration()
    {
        var blocks = Blocks();
        var fluids = Fluids();
        var chunk = new Chunk();
        chunk.SetFluid(2, 2, 2,
            FluidCell.Source(fluids.GetId("asteria:water")));
        chunk.SetBlock(3, 2, 2, blocks.GetId("asteria:test_sprite"));
        var world = new VoxelWorld();
        world.ConfigureFluidOccupancy(blocks);
        world.InsertChunk(ChunkCoord.Zero, chunk);

        var mesh = FluidMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks, fluids, 0);

        Assert.Contains(mesh.Batches.SelectMany(batch => batch.Vertices),
            vertex => vertex.Normal == Vector3.UnitX &&
                      vertex.Position.X == 3f);

        // Solid neighbors still occlude the submerged face.
        Assert.True(world.SetBlockAt(NeighborPosition,
            blocks.GetId("asteria:stone"), out _));
        var blocked = FluidMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks, fluids, 0);
        Assert.DoesNotContain(blocked.Batches.SelectMany(batch => batch.Vertices),
            vertex => vertex.Normal == Vector3.UnitX &&
                      vertex.Position.X == 3f);
    }

    [Fact]
    public void WaterSourceSurvivesSpritePlacementAndRemoval()
    {
        var blocks = Blocks();
        var fluids = Fluids();
        var source = FluidCell.Source(fluids.GetId("asteria:water"));
        var world = new VoxelWorld();
        world.ConfigureFluidOccupancy(blocks);
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        Assert.True(world.SetFluidAt(WaterPosition, source, out _));
        Assert.True(world.SetBlockAt(WaterPosition,
            blocks.GetId("asteria:test_sprite"), out _));
        Assert.Equal(source, world.GetFluidOrEmpty(WaterPosition));
        Assert.Equal(source, FluidSimulationSolver.DesiredFluid(
            world, fluids, WaterPosition, source));

        var snapshot = world.CloneForWorker();
        Assert.True(snapshot.CanContainFluidAt(WaterPosition));
        Assert.Equal(source, snapshot.GetFluidOrEmpty(WaterPosition));
        var mesh = FluidMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks, fluids, 0);
        Assert.Contains(mesh.Batches.SelectMany(batch => batch.Vertices),
            vertex => vertex.Normal == Vector3.UnitY && vertex.Position.Y == 3f);

        Assert.True(world.SetBlockAt(WaterPosition, BlockRuntimeId.Air, out _));
        Assert.Equal(source, world.GetFluidOrEmpty(WaterPosition));
        Assert.True(world.SetBlockAt(WaterPosition,
            blocks.GetId("asteria:stone"), out _));
        Assert.True(world.GetFluidOrEmpty(WaterPosition).IsEmpty);
        Assert.False(world.SetFluidAt(WaterPosition, source, out _));
    }

    [Fact]
    public void GeneratedSeagrassObjectDoesNotReplaceWaterWithABlock()
    {
        var blocks = Blocks();
        var fluids = Fluids();
        var objects = PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {
              "id": "asteria:seagrass",
              "category": "foliage",
              "icon": "textures/objects/seagrass.png",
              "placementFaces": ["top"],
              "visual": {
                "type": "crossedSprite",
                "texture": "textures/objects/seagrass.png"
              }
            }
            """
        ], ObjectDefinition.Parse, definition => definition.Id);
        var biome = new BiomeDefinition(
            "asteria:test/ocean",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(-18f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition("asteria:seagrass",
                    1f, ["asteria:stone"],
                    fluidPlacement: DecorationFluidPlacement.Submerged,
                    isObject: true)
            ]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [biome.Id], seaLevel: 32, gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            generatedOcean: new DimensionGeneratedOceanDefinition(
                biome.Id, "asteria:water"));
        var generator = new BiomeWorldGenerator(
            17, dimension, blocks, fluids, new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(spawnCaves: false),
            objects: objects);

        var supportY = generator.SurfaceHeight(8, 8);
        var support = generator.Materialize(
            new ChunkCoord(0, supportY / Chunk.Size, 0));
        Assert.Equal("asteria:seagrass",
            Assert.Single(support.ObjectsAt(8, supportY % Chunk.Size, 8)).Id);

        var fluidY = supportY + 1;
        var water = generator.Materialize(
            new ChunkCoord(0, fluidY / Chunk.Size, 0));
        Assert.True(water.GetCell(8, fluidY % Chunk.Size, 8).IsEmpty);
        Assert.Equal(FluidCell.Source(fluids.GetId("asteria:water")),
            water.GetFluid(8, fluidY % Chunk.Size, 8));
    }

    private static BlockRegistry Blocks() => new([
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:test_sprite",
            visual: BlockVisualDefinition.CrossedSprite(
                new BlockTextureLayer("textures/objects/grass.png")),
            isCollidable: false, lightDampening: 0)
    ]);

    private static FluidRegistry Fluids() => new([
        new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), 0.7f)
    ]);
}
