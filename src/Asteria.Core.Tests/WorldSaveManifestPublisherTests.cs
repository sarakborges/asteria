using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldSaveManifestPublisherTests
{
    private static DimensionDefinition Dimension() => new(
        DimensionId.Overworld, ["asteria:overworld/plains"],
        90, 18f, new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0),
            new DimensionColor(255, 255, 255),
            1f, new DimensionColor(0, 0, 0), 0f));

    [Fact]
    public void PublishedCompleteSessionIsDiscoverableButNotClaimedPlayable()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "asteria-manifest-" + Guid.NewGuid().ToString("N"));
        var world = Path.Combine(directory, "My World");
        try
        {
            var dimensions = new DimensionRegistry([Dimension()]);
            var creation = new WorldCreationOptions("My World", 987654321UL);
            var states = new DimensionSessionStateStore(creation, dimensions);
            var overworld = states.GetOrCreate(DimensionId.Overworld);
            overworld.PlayerPosition = new Vector3(-4.5f, 82, 15.5f);
            overworld.DayNight = new DayNightClockState(2, 10);
            var blocks = new BlockRegistry([]);
            var fluids = new FluidRegistry([]);
            var dyes = new DyeRegistry([]);
            var layers = new AttachedLayerRegistry([]);
            var saved = GameplaySessionSaveCodec.Capture(
                states, blocks, fluids, dyes, layers, DimensionId.Overworld);
            var generation = SessionSaveStorage.Publish(
                world, saved, blocks, fluids, dyes, layers);
            WorldSaveManifestPublisher.Publish(world, saved, generation);

            var listed = Assert.Single(WorldSaveCatalog.Scan(directory));
            Assert.Equal(creation.Name, listed.Id);
            Assert.Equal(WorldCreationSeed.Format(creation.Seed), listed.Seed);
            Assert.Equal("asteria:overworld", listed.Sphere);
            Assert.Equal("1", listed.DaysPassed);
            Assert.False(listed.Compatible);
            Assert.Contains("X: -5", listed.Coordinates);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsWrongDirectoryWithoutPublishingMisleadingManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "asteria-manifest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var creation = new WorldCreationOptions("My World", 1UL);
            var state = new DimensionSessionStateStore(
                creation, new DimensionRegistry([Dimension()]));
            state.GetOrCreate(DimensionId.Overworld);
            var saved = GameplaySessionSaveCodec.Capture(
                state, new BlockRegistry([]), new FluidRegistry([]),
                new DyeRegistry([]), new AttachedLayerRegistry([]),
                activeSphere: DimensionId.Overworld);
            Assert.Throws<InvalidDataException>(() =>
                WorldSaveManifestPublisher.Publish(directory, saved, 1));
            Assert.False(File.Exists(Path.Combine(directory,
                WorldSaveCatalog.ManifestFileName)));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
