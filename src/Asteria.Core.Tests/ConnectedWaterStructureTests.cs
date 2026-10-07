using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ConnectedWaterStructureTests
{
    [Fact]
    public void DefaultConnectedWaterStructuresParseAndValidate()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var fluids =
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));

        structures.ValidateBlocks(
            blocks);
        structures.ValidateFluids(
            fluids);
        dimensions.ValidateStructures(
            structures);

        Assert.Equal(
            10,
            structures.ResolveReference(
                "asteria:river_segment")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:mountain_pond")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:mountain_waterfall")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:river_lake")
                .Count);

        var waterfall =
            structures.Get(
                "asteria:mountain_waterfall_01");
        Assert.Equal(
            3,
            waterfall.Restrictions.MinSlope);
        Assert.Equal(
            -1,
            waterfall.GroundAnchorY);
        Assert.Equal(
            2,
            waterfall.ClearAbove);
        Assert.NotEmpty(
            waterfall.FluidVoxels);

        var mouth =
            structures.Get(
                "asteria:river_ocean_mouth");
        Assert.NotEmpty(
            mouth.FluidVoxels);
        Assert.NotEmpty(
            mouth.ClearVoxels);
        Assert.Contains(
            mouth.Connectors,
            connector =>
                string.Equals(
                    connector.Target,
                    "asteria:river_segment",
                    StringComparison.Ordinal));

        var overworld =
            dimensions.Get(
                DimensionId.Overworld);
        var mouthRoot =
            Assert.Single(
                overworld
                    .GeneratedSurfaceStructures
                    .Where(root =>
                        string.Equals(
                            root.Structure,
                            "asteria:river_ocean_mouth",
                            StringComparison.Ordinal)));
        Assert.Equal(
            DimensionGeneratedSurfaceStructurePlacement.BiomeMargin,
            mouthRoot.Placement);
    }

    private static IEnumerable<string> ReadJsonDirectory(
        string category)
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                category);

        return Directory
            .EnumerateFiles(
                directory,
                "*.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText);
    }
}
