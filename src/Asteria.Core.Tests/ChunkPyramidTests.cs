using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkPyramidTests
{
    [Fact]
    public void ChunkStatusesMatchMinecraft263OrderAndChunkTypes()
    {
        var expectedNames = new[]
        {
            "empty",
            "structure_starts",
            "structure_references",
            "biomes",
            "terrain",
            "features",
            "initialize_light",
            "light",
            "spawn",
            "full",
        };

        Assert.Equal(expectedNames, ChunkStatus.All.Select(status => status.Name));
        Assert.Equal(
            ChunkStatus.All.Length,
            ChunkStatus.All.Select(status => status.Index).Distinct().Count());

        for (var index = 0; index < ChunkStatus.All.Length; index++)
        {
            var status = ChunkStatus.All[index];
            Assert.Equal(index, status.Index);
            if (index > 0)
                Assert.Same(ChunkStatus.All[index - 1], status.Parent);
        }

        Assert.All(
            ChunkStatus.All.Take(9),
            status => Assert.Equal(ChunkType.ProtoChunk, status.ChunkType));
        Assert.Equal(ChunkType.LevelChunk, ChunkStatus.Full.ChunkType);
        Assert.Same(ChunkStatus.Terrain, ChunkStatus.GetByName("terrain"));
        Assert.Throws<KeyNotFoundException>(
            () => ChunkStatus.GetByName("noise"));
    }

    [Fact]
    public void ChunkStatusesExposeVanillaHeightmapsAfterEachLifecyclePhase()
    {
        var worldgenHeightmaps = new[]
        {
            ChunkHeightmap.OceanFloorWorldgen,
            ChunkHeightmap.WorldSurfaceWorldgen,
        };
        var finalHeightmaps = new[]
        {
            ChunkHeightmap.OceanFloor,
            ChunkHeightmap.WorldSurface,
            ChunkHeightmap.MotionBlocking,
            ChunkHeightmap.MotionBlockingNoLeaves,
        };

        foreach (var status in ChunkStatus.All.Take(4))
            Assert.Equal(worldgenHeightmaps, status.HeightmapsAfter);

        foreach (var status in ChunkStatus.All.Skip(4))
            Assert.Equal(finalHeightmaps, status.HeightmapsAfter);
    }

    [Fact]
    public void GenerationPyramidMatchesMinecraft263DirectDependencies()
    {
        var starts = Repeat("structure_starts", 8);
        var expected = new[]
        {
            Array.Empty<string>(),
            new[] { "empty" },
            Repeat("structure_starts", 9),
            new[] { "structure_references" }.Concat(starts).ToArray(),
            new[] { "biomes", "biomes" }.Concat(Repeat("structure_starts", 7)).ToArray(),
            new[] { "terrain", "terrain" }.Concat(Repeat("structure_starts", 7)).ToArray(),
            new[] { "features" },
            new[] { "initialize_light", "initialize_light" },
            new[] { "light", "biomes" },
            new[] { "spawn" },
        };

        AssertPyramidDependencies(ChunkPyramid.Generation, expected, accumulated: false);
    }

    [Fact]
    public void GenerationPyramidComposesAccumulatedDependenciesByParentRadius()
    {
        var starts = Repeat("structure_starts", 8);
        var expected = new[]
        {
            Array.Empty<string>(),
            new[] { "empty" },
            Repeat("structure_starts", 9),
            new[] { "structure_references" }.Concat(starts).ToArray(),
            new[] { "biomes", "biomes" }.Concat(Repeat("structure_starts", 8)).ToArray(),
            new[] { "terrain", "terrain", "biomes" }.Concat(Repeat("structure_starts", 8)).ToArray(),
            new[] { "features", "terrain", "biomes" }.Concat(Repeat("structure_starts", 8)).ToArray(),
            new[] { "initialize_light", "initialize_light", "terrain", "biomes" }
                .Concat(Repeat("structure_starts", 8)).ToArray(),
            new[] { "light", "initialize_light", "terrain", "biomes" }
                .Concat(Repeat("structure_starts", 8)).ToArray(),
            new[] { "spawn", "initialize_light", "terrain", "biomes" }
                .Concat(Repeat("structure_starts", 8)).ToArray(),
        };

        AssertPyramidDependencies(ChunkPyramid.Generation, expected, accumulated: true);

        var fullDependencies = ChunkPyramid.Generation.FullStep.AccumulatedDependencies;
        Assert.Equal(11, fullDependencies.Radius);
        Assert.Equal(11, fullDependencies.GetRadiusOf(ChunkStatus.StructureStarts));
        Assert.Equal(3, fullDependencies.GetRadiusOf(ChunkStatus.Biomes));
        Assert.Equal(2, fullDependencies.GetRadiusOf(ChunkStatus.Terrain));
        Assert.Equal(1, fullDependencies.GetRadiusOf(ChunkStatus.InitializeLight));
        Assert.Equal(0, fullDependencies.GetRadiusOf(ChunkStatus.Spawn));
    }

    [Fact]
    public void LoadingPyramidPassesThroughGenerationOnlyStatuses()
    {
        var expectedDirect = new[]
        {
            Array.Empty<string>(),
            new[] { "empty" },
            new[] { "structure_starts" },
            new[] { "structure_references" },
            new[] { "biomes" },
            new[] { "terrain" },
            new[] { "features" },
            new[] { "initialize_light", "initialize_light" },
            new[] { "light" },
            new[] { "spawn" },
        };

        AssertPyramidDependencies(
            ChunkPyramid.Loading,
            expectedDirect,
            accumulated: false);

        Assert.Equal(
            new[] { "spawn", "initialize_light" },
            Names(ChunkPyramid.Loading.FullStep.AccumulatedDependencies));
        Assert.Equal(1, ChunkPyramid.Loading.FullStep.AccumulatedDependencies.Radius);
    }


    [Fact]
    public void FullChunkStepRadiusMatchesMinecraftTicketLevelMapping()
    {
        var fullStep = ChunkPyramid.Generation.FullStep;

        Assert.Equal(0, fullStep.GetAccumulatedRadiusOf(ChunkStatus.Full));
        Assert.Equal(11, fullStep.GetAccumulatedRadiusOf(ChunkStatus.Empty));
        Assert.Equal(44, ChunkLevel.MaximumLoading);
        Assert.Equal(44, ChunkLevel.GetTicketLevel(ChunkStatus.Empty));
        Assert.Equal(44, ChunkLevel.GetTicketLevel(ChunkStatus.StructureStarts));
        Assert.Equal(36, ChunkLevel.GetTicketLevel(ChunkStatus.StructureReferences));
        Assert.Equal(36, ChunkLevel.GetTicketLevel(ChunkStatus.Biomes));
        Assert.Equal(35, ChunkLevel.GetTicketLevel(ChunkStatus.Terrain));
        Assert.Equal(34, ChunkLevel.GetTicketLevel(ChunkStatus.Features));
        Assert.Equal(34, ChunkLevel.GetTicketLevel(ChunkStatus.InitializeLight));
        Assert.Equal(33, ChunkLevel.GetTicketLevel(ChunkStatus.Light));
        Assert.Equal(33, ChunkLevel.GetTicketLevel(ChunkStatus.Spawn));
        Assert.Equal(33, ChunkLevel.GetTicketLevel(ChunkStatus.Full));
    }

    [Fact]
    public void TicketLevelsResolveThroughTheFullGenerationDependencyPyramid()
    {
        Assert.Same(ChunkStatus.Full, ChunkLevel.GetChunkGenerationStatus(32));
        Assert.Same(ChunkStatus.Full, ChunkLevel.GetChunkGenerationStatus(33));
        Assert.Same(ChunkStatus.InitializeLight, ChunkLevel.GetChunkGenerationStatus(34));
        Assert.Same(ChunkStatus.Terrain, ChunkLevel.GetChunkGenerationStatus(35));
        Assert.Same(ChunkStatus.Biomes, ChunkLevel.GetChunkGenerationStatus(36));

        for (var level = 37; level <= 44; level++)
            Assert.Same(
                ChunkStatus.StructureStarts,
                ChunkLevel.GetChunkGenerationStatus(level));

        Assert.Null(ChunkLevel.GetChunkGenerationStatus(45));
    }

    [Fact]
    public void OnlyTerrainAndFeaturesMayWriteThroughGenerationRegion()
    {
        var expectedWriteRadii = new[] { -1, -1, -1, -1, 0, 1, -1, -1, -1, -1 };

        Assert.Equal(
            expectedWriteRadii,
            ChunkPyramid.Generation.Steps
                .Select(step => step.BlockStateWriteRadius));
    }

    private static void AssertPyramidDependencies(
        ChunkPyramid pyramid,
        IReadOnlyList<string[]> expected,
        bool accumulated)
    {
        Assert.Equal(ChunkStatus.All.Length, expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var step = pyramid.Steps[index];
            var actual = accumulated
                ? Names(step.AccumulatedDependencies)
                : Names(step.DirectDependencies);
            Assert.Equal(expected[index], actual);
        }
    }

    private static string[] Names(ChunkDependencies dependencies) =>
        dependencies.RequiredStatusesByDistance
            .Select(status => status.Name)
            .ToArray();

    private static string[] Repeat(string value, int count) =>
        Enumerable.Repeat(value, count).ToArray();
}
