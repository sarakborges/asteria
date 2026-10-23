using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidLightingTests
{
    [Fact]
    public void FluidRelightFallsBackWhenPreviousQuadCornerOrderIsInvalid()
    {
        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water",
                new FluidColor(64, 128, 224), opacity: 0.75f),
        ]);
        var water = fluids.GetId("asteria:water");
        var chunk = new Chunk();
        chunk.SetFluid(3, 8, 3, FluidCell.Source(water));
        chunk.FillLight(new VoxelLight(7, 0, 0, 0));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var original = FluidMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks, fluids, 4);

        var batches = original.Batches.ToArray();
        var vertices = batches[0].Vertices.ToArray();
        var sample = vertices[0].LightSample;
        vertices[0] = vertices[0] with
        {
            LightSample = new ChunkMeshLightSample(
                sample.Source, sample.Face, (byte)((sample.Corner + 1) % 4)),
        };
        batches[0] = new ChunkFluidBatchData(batches[0].Fluid, vertices);
        var invalid = new ChunkFluidMeshData(batches);

        Assert.False(FluidMeshLightingUpdater.TryRelight(
            world, ChunkCoord.Zero, blocks, invalid, out _));
    }

    [Theory]
    [InlineData(15, 1, 2)]
    [InlineData(15, 4, 8)]
    [InlineData(15, 8, 15)]
    public void FluidDampeningScalesWithFillLevel(
        byte fullDampening,
        byte level,
        byte expected)
    {
        Assert.Equal(
            expected,
            VoxelLightingMedium.ScaleFluidDampening(
                fullDampening,
                level));
    }

    [Theory]
    [InlineData(15, 1, 2)]
    [InlineData(15, 4, 8)]
    [InlineData(15, 8, 15)]
    public void FluidEmissionScalesWithFillLevel(
        byte fullEmission,
        byte level,
        byte expected)
    {
        var scaled =
            VoxelLightingMedium.ScaleFluidEmission(
                new BlockLightEmission(
                    fullEmission,
                    0,
                    0),
                level);

        Assert.Equal(expected, scaled.Red);
        Assert.Equal((byte)0, scaled.Green);
        Assert.Equal((byte)0, scaled.Blue);
    }

    [Fact]
    public void FluidLightOnlyUpdatePreservesQuadGeometryWhenWindingIsCurrent()
    {
        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water",
                new FluidColor(64, 128, 224), opacity: 0.75f),
        ]);
        var water = fluids.GetId("asteria:water");
        var chunk = new Chunk();
        chunk.SetFluid(3, 8, 3, FluidCell.Source(water));
        // Uniform lighting before and after the change preserves the quad
        // diagonal, making the geometry-only parity assertion unconditional.
        chunk.FillLight(new VoxelLight(7, 0, 0, 0));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var original = FluidMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks, fluids, 4);
        Assert.True(original.HasGeometry);
        chunk.FillLight(new VoxelLight(12, 5, 1, 0));

        Assert.True(FluidMeshLightingUpdater.TryRelight(
                world, ChunkCoord.Zero, blocks, original, out var updated));

        var rebuilt = FluidMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks, fluids, 4);
        Assert.Equal(rebuilt.Batches.Length, updated.Batches.Length);
        for (var i = 0; i < rebuilt.Batches.Length; i++)
        {
            Assert.Equal(rebuilt.Batches[i].Fluid, updated.Batches[i].Fluid);
            Assert.Equal(rebuilt.Batches[i].Vertices, updated.Batches[i].Vertices);
        }
    }

    [Fact]
    public void EmissiveFluidPropagatesColoredLight()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:glow",
                    new FluidColor(255, 32, 32),
                    opacity: 0.7f,
                    lightEmission:
                        new BlockLightEmission(
                            15,
                            0,
                            0)),
            ]);
        var glow =
            fluids.GetId("asteria:glow");
        var chunk = new Chunk();

        chunk.SetFluid(
            8,
            8,
            8,
            FluidCell.Source(glow));

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        Assert.Equal(
            (byte)15,
            chunk.GetLight(
                8,
                8,
                8).Red);
        Assert.Equal(
            (byte)14,
            chunk.GetLight(
                9,
                8,
                8).Red);
    }

    [Fact]
    public void IncrementalEmissiveFluidPlacementAndRemovalRelights()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:glow",
                    new FluidColor(32, 64, 255),
                    opacity: 0.7f,
                    lightEmission:
                        new BlockLightEmission(
                            0,
                            0,
                            15)),
            ]);
        var glow =
            fluids.GetId("asteria:glow");
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            fluids);

        var source =
            new WorldVoxelCoord(8, 8, 8);
        var neighbor =
            source + (1, 0, 0);

        Assert.True(
            world.SetFluidAt(
                source,
                FluidCell.Source(glow),
                out _));

        VoxelWorldLightingSolver.RelightAfterEdits(
            world,
            blocks,
            fluids,
            [source]);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(source).Blue);
        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(neighbor).Blue);

        Assert.True(
            world.SetFluidAt(
                source,
                FluidCell.Empty,
                out _));

        VoxelWorldLightingSolver.RelightAfterEdits(
            world,
            blocks,
            fluids,
            [source]);

        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(source).Blue);
        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(neighbor).Blue);
    }

    [Fact]
    public void FluidJsonParsesRgbEmission()
    {
        const string json = """
            {
              "id": "asteria:glow",
              "color": "2040ff",
              "opacity": 0.7,
              "lightEmission": {
                "red": 2,
                "green": 4,
                "blue": 15
              }
            }
            """;

        var registry =
            FluidRegistry.FromJson([json]);
        var definition =
            registry.GetDefinition(
                registry.GetId(
                    "asteria:glow"));

        Assert.Equal(
            new BlockLightEmission(
                2,
                4,
                15),
            definition.LightEmission);
    }

    [Fact]
    public void FullWaterLayerReducesDirectSkyBelowIt()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            CreateWater(
                lightDampening: 1);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        for (var z = 0;
             z < Chunk.Size;
             z++)
        {
            for (var x = 0;
                 x < Chunk.Size;
                 x++)
            {
                chunk.SetFluid(
                    x,
                    10,
                    z,
                    FluidCell.Source(water));
            }
        }

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        Assert.Equal(
            (byte)14,
            chunk.GetLight(
                8,
                10,
                8).Sky);
        Assert.Equal(
            (byte)14,
            chunk.GetLight(
                8,
                9,
                8).Sky);
    }

    [Fact]
    public void BlockLightAttenuatesThroughFluidMedium()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:red_lamp",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            15,
                            0,
                            0)),
            ]);
        var lamp =
            blocks.GetId("asteria:red_lamp");
        var fluids =
            CreateWater(
                lightDampening: 3);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        chunk.SetBlock(
            7,
            8,
            7,
            lamp);
        chunk.SetFluid(
            8,
            8,
            7,
            FluidCell.Source(water));

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        Assert.Equal(
            (byte)12,
            chunk.GetLight(
                8,
                8,
                7).Red);
    }

    [Fact]
    public void IncrementalFluidPlacementAndRemovalRelightsWithoutFullReset()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            CreateWater(
                lightDampening: 1);
        var water =
            fluids.GetId("asteria:water");
        var world =
            new VoxelWorld();

        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            fluids);

        var position =
            new WorldVoxelCoord(
                8,
                10,
                8);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(
                position).Sky);

        world.SetFluidAt(
            position,
            FluidCell.Source(water),
            out _);

        var darken =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                fluids,
                [position]);

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                position).Sky);
        Assert.Contains(
            position,
            darken.ChangedPositions);

        world.SetFluidAt(
            position,
            FluidCell.Empty,
            out _);

        var brighten =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                fluids,
                [position]);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(
                position).Sky);
        Assert.Contains(
            position,
            brighten.ChangedPositions);
    }

    [Fact]
    public void WaterChunkWithDifferentFlowStatesSkipsStableLightFloodFill()
    {
        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([
            new FluidDefinition(
                "asteria:water",
                new FluidColor(64, 128, 224),
                opacity: 0.72f,
                lightDampening: 1),
        ]);
        var water = fluids.GetId("asteria:water");
        var chunk = new Chunk();

        for (var y = 0; y < Chunk.Size; y++)
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var level = (byte)(1 + ((x + y + z) % FluidCell.MaxLevel));
            var fluid = (x + y + z) % 2 == 0
                ? FluidCell.Source(water, level)
                : FluidCell.Spreading(water, level, (ushort)(x + y + z));
            chunk.SetFluid(x, y, z, fluid);
        }

        var processed = ChunkLightingSolver.InitializeAndCountProcessedVoxels(
            chunk, blocks, fluids);

        // Water light attenuation rounds to one for all levels here. Although
        // the actual flow/source state differs throughout the chunk, every X/Z
        // column has the same direct-sky field and needs no flood-fill.
        Assert.Equal(0, processed);
        Assert.Equal((byte)14, chunk.GetLight(8, 15, 8).Sky);
        Assert.Equal((byte)13, chunk.GetLight(8, 14, 8).Sky);
        Assert.Equal((byte)0, chunk.GetLight(8, 0, 8).Sky);
    }

    [Fact]
    public void WaterCanStillReceiveLateralLightFromAnAirShaft()
    {
        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([
            new FluidDefinition(
                "asteria:water",
                new FluidColor(64, 128, 224),
                opacity: 0.72f,
                lightDampening: 1),
        ]);
        var water = fluids.GetId("asteria:water");
        var chunk = new Chunk();

        for (var y = 0; y < Chunk.Size; y++)
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            if (x != 8)
                chunk.SetFluid(x, y, z, FluidCell.Source(water));
        }

        var processed = ChunkLightingSolver.InitializeAndCountProcessedVoxels(
            chunk, blocks, fluids);

        Assert.True(processed > 0);
        Assert.Equal((byte)15, chunk.GetLight(8, 14, 8).Sky);
        Assert.True(chunk.GetLight(7, 14, 8).Sky > (byte)13);
    }

    private static FluidRegistry CreateWater(
        byte lightDampening) =>
        new(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(
                    79,
                    159,
                    214),
                opacity: 0.72f,
                lightDampening:
                    lightDampening,
                spreadSpeed: 5f,
                maxSpread: 7),
        ]);
}
