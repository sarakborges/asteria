using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SkyLayerPatternTests
{
    [Fact]
    public void AllAuthoredShapesStayFiniteAndBounded()
    {
        for (var i = 0; i < SkyLayerPattern.StarCount; i++)
        {
            var star = SkyLayerPattern.StarAt(i);
            Assert.InRange(star.Y, 0.08f, 0.95f);
            Assert.InRange(star.Size, 0.16f, 0.35f);
            Assert.True(float.IsFinite(star.X + star.Z));
        }

        for (var i = 0; i < SkyLayerPattern.CloudCount; i++)
        for (var part = 0; part < SkyLayerPattern.CloudPartsPerCloud; part++)
        {
            var cloud = SkyLayerPattern.CloudPartAt(i, part);
            Assert.Equal(cloud, SkyLayerPattern.CloudPartAt(i, part));
            Assert.InRange(cloud.AltitudeAboveSeaLevel, 34f, 48f);
            Assert.True(cloud.Width > 0 && cloud.Depth > 0);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyLayerPattern.StarAt(96));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyLayerPattern.CloudPartAt(24, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyLayerPattern.CloudPartAt(0, 3));
    }

    [Fact]
    public void CameraMovesWithinWorldTileWithoutDraggingClouds()
    {
        Assert.Equal(20f, SkyLayerPattern.WorldTileCoordinate(12f, 8f, 0f));
        Assert.Equal(20f, SkyLayerPattern.WorldTileCoordinate(12f, 8f, 30f));
        Assert.Equal(190f, SkyLayerPattern.WorldTileCoordinate(10f, 0f, 200f));
        Assert.Equal(12f, SkyLayerPattern.WorldTileCoordinate(10f, 2f, 0f));
    }

    [Fact]
    public void StarsFadeOnlyAtNightAndSkyLayersArePackAuthored()
    {
        Assert.Equal(0f, SkyLayerPattern.StarVisibility(
            new DayNightSample(DayNightPhase.Day, DayNightPhase.Dusk, 0.5, 1f, 0.3)));
        Assert.Equal(0.5f, SkyLayerPattern.StarVisibility(
            new DayNightSample(DayNightPhase.Dusk, DayNightPhase.Night, 0.5, 0.3f, 0.6)));
        Assert.Equal(0.75f, SkyLayerPattern.StarVisibility(
            new DayNightSample(DayNightPhase.Night, DayNightPhase.Dawn, 0.25, 0.06f, 0.8)));

        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "packs", "default",
                "data", "dimensions", "overworld.json"));
        var dimension = DimensionDefinitionJson.Parse(source);
        Assert.Equal(1f, dimension.Environment.SkyLayers.StarDensity);
        Assert.Equal(0.8f, dimension.Environment.SkyLayers.CloudDensity);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DimensionSkyLayersDefinition(float.NaN,
                new DimensionColor(255, 255, 255), 0.5f,
                new DimensionColor(255, 255, 255)));
    }
}
