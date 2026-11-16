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
            structures,
            StructureSetRegistry.FromJson(
                ReadJsonDirectory("structure_sets")));

        Assert.Equal(
            10,
            structures.ResolveReference(
                "asteria:segment_river")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:pond_mountain")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:waterfall_mountain")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake_river")
                .Count);

        var waterfall =
            structures.Get(
                "asteria:waterfall_mountain_01");
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
                "asteria:mouth_river_ocean");
        Assert.NotEmpty(
            mouth.FluidVoxels);
        Assert.NotEmpty(
            mouth.ClearVoxels);
        Assert.Contains(
            mouth.Connectors,
            connector =>
                string.Equals(
                    connector.Target,
                    "asteria:segment_river",
                    StringComparison.Ordinal));

        var overworld =
            dimensions.Get(
                DimensionId.Overworld);
        var mouthRoot =
            Assert.Single(
                overworld.GeneratedSurfaceStructures,
                root =>
                    string.Equals(
                        root.Structure,
                        "asteria:mouth_river_ocean",
                        StringComparison.Ordinal));
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
