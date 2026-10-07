using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceStructureTests
{
    [Fact]
    public void DefaultStructurePackParsesAndValidatesPhaseOneContent()
    {
        var blocks =
            LoadDefaultBlocks();
        var structures =
            LoadDefaultStructures();
        var dimensions =
            LoadDefaultDimensions();

        structures.ValidateBlocks(
            blocks);
        dimensions.ValidateStructures(
            structures);

        Assert.Equal(
            8,
            structures.Count);

        var oak =
            structures.ReferenceMembers(
                "asteria:tree_oak");

        Assert.Equal(
            4,
            oak.Count);
        Assert.Equal(
            new[]
            {
                "asteria:tree_oak_01",
                "asteria:tree_oak_02",
                "asteria:tree_oak_03",
                "asteria:tree_oak_04",
            },
            oak.Select(
                definition =>
                    definition.Id));

        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        Assert.Equal(
            23,
            overworld.GeneratedSurfaceStructures.Count);
        Assert.All(
            overworld.GeneratedSurfaceStructures,
            rule =>
            {
                Assert.Contains(
                    rule.Biome,
                    overworld.SurfaceBiomes);
                Assert.Equal(
                    GeneratedSurfaceStructurePlacement.BiomeInterior,
                    rule.Placement);
                _ =
                    structures.ReferenceMembers(
                        rule.Structure);
            });

        Assert.DoesNotContain(
            overworld.GeneratedSurfaceStructures,
            rule =>
                rule.Structure.Contains(
                    "river",
                    StringComparison.Ordinal) ||
                rule.Structure.Contains(
                    "lake",
                    StringComparison.Ordinal) ||
                rule.Structure.Contains(
                    "waterfall",
                    StringComparison.Ordinal) ||
                rule.Structure.Contains(
                    "willow",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void OakPlacementsAreDeterministicAndRasterizeStructureBlocks()
    {
        var generator =
            DefaultOverworldGenerator(
                out var blocks);
        var first =
            generator.Structures
                .PlacementsIntersecting(
                    -2048,
                    -2048,
                    4096,
                    4096)
                .Where(
                    placement =>
                        placement.Reference ==
                        "asteria:tree_oak")
                .ToArray();

        Assert.NotEmpty(
            first);

        var repeated =
            generator.Structures
                .PlacementsIntersecting(
                    -2048,
                    -2048,
                    4096,
                    4096)
                .Where(
                    placement =>
                        placement.Reference ==
                        "asteria:tree_oak")
                .Select(
                    PlacementIdentity)
                .ToArray();

        Assert.Equal(
            first
                .Select(
                    PlacementIdentity)
                .ToArray(),
            repeated);

        Assert.All(
            first.Take(32),
            placement =>
                Assert.Equal(
                    "asteria:overworld/plains",
                    generator.Biomes
                        .Sample(
                            placement.AnchorX,
                            placement.AnchorZ)
                        .Primary));

        var oakLog =
            blocks.GetId(
                "asteria:log_oak");
        var foundRasterizedLog =
            false;

        foreach (var placement in
                 first.Take(64))
        {
            foreach (var voxel in
                     placement.Structure
                         .RotatedVoxels(
                             placement.Rotation)
                         .Where(voxel =>
                             voxel.Block ==
                             "asteria:log_oak"))
            {
                var worldX =
                    checked(
                        placement.OriginX +
                        voxel.Offset.X);
                var worldY =
                    checked(
                        placement.OriginY +
                        voxel.Offset.Y);
                var worldZ =
                    checked(
                        placement.OriginZ +
                        voxel.Offset.Z);
                var address =
                    VoxelCoordinates.FromWorld(
                        worldX,
                        worldY,
                        worldZ);
                var chunk =
                    generator.Materialize(
                        address.Chunk);
                var cell =
                    chunk.GetCell(
                        address.Local.X,
                        address.Local.Y,
                        address.Local.Z);

                if (cell.Block !=
                    oakLog)
                {
                    continue;
                }

                Assert.Equal(
                    voxel.Orientation,
                    cell.Orientation);
                foundRasterizedLog =
                    true;
                break;
            }

            if (foundRasterizedLog)
            {
                break;
            }
        }

        Assert.True(
            foundRasterizedLog,
            "Expected at least one authoritative oak placement to rasterize an oak log voxel.");
    }

    [Fact]
    public void StructureRangeIncludesAcceptedPlacementHeight()
    {
        var generator =
            DefaultOverworldGenerator(
                out _);
        var placement =
            generator.Structures
                .PlacementsIntersecting(
                    -2048,
                    -2048,
                    4096,
                    4096)
                .First();

        var bounds =
            placement.HorizontalBounds();
        var chunk =
            VoxelCoordinates.FromWorld(
                    bounds.MinX,
                    0,
                    bounds.MinZ)
                .Chunk;
        var range =
            generator.GetSurfaceRange(
                chunk.X,
                chunk.Z);

        Assert.True(
            range.MaximumWorldY >=
            placement.MaximumY);
    }

    private static string PlacementIdentity(
        SurfaceStructurePlacement placement) =>
        string.Join(
            '|',
            placement.Reference,
            placement.Structure.Id,
            placement.AnchorX,
            placement.AnchorZ,
            placement.Rotation,
            placement.OriginX,
            placement.OriginY,
            placement.OriginZ);

    private static BiomeWorldGenerator DefaultOverworldGenerator(
        out BlockRegistry blocks)
    {
        const ulong worldSeed =
            0xA57E_2026UL;

        blocks =
            LoadDefaultBlocks();
        var fluids =
            LoadDefaultFluids();
        var biomes =
            LoadDefaultBiomes();
        var structures =
            LoadDefaultStructures();
        var dimensions =
            LoadDefaultDimensions();

        dimensions.ValidateBlocks(
            blocks);
        dimensions.ValidateFluids(
            fluids);
        dimensions.ValidateBiomes(
            biomes);
        structures.ValidateBlocks(
            blocks);
        dimensions.ValidateStructures(
            structures);

        var dimension =
            dimensions.Get(
                DimensionId.Overworld);

        return new BiomeWorldGenerator(
            DimensionSeed.Derive(
                worldSeed,
                dimension.Id),
            dimension,
            blocks,
            fluids,
            biomes,
            structures);
    }

    private static BlockRegistry LoadDefaultBlocks() =>
        BlockRegistry.FromJson(
            ReadJsonDirectory(
                "blocks"));

    private static FluidRegistry LoadDefaultFluids() =>
        FluidRegistry.FromJson(
            ReadJsonDirectory(
                "fluids"));

    private static BiomeRegistry LoadDefaultBiomes() =>
        BiomeRegistry.FromJson(
            ReadJsonDirectory(
                "biomes"));

    private static StructureRegistry LoadDefaultStructures() =>
        StructureRegistry.FromJson(
            ReadJsonDirectory(
                "structures"));

    private static DimensionRegistry LoadDefaultDimensions() =>
        DimensionRegistry.FromJson(
            ReadJsonDirectory(
                "dimensions"));

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
                path =>
                    path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText);
    }
}
