using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureDetailedPlacementTests
{
    [Fact]
    public void GeneratedStructureRestoresMaskDyeFaceLayersAndCellProperties()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:sculptable",
                tags: ["fragmentable"], secondaryProperties: ["dyed"])
        ]);
        var mask = MicroblockMask.Full.Edit(0, 1, 2,
            MicroblockResolution.ExtraThin, occupied: false);
        var surface = new BlockSurfaceState("asteria:red", [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss"),
            new AttachedBlockLayer(BlockFace.Front, "asteria:ivy",
                TextureRotation.Degrees270)
        ]);
        var detail = new StructureVoxelState(
            TextureRotation.Degrees90, HorizontalFacing.North,
            11, mask, surface);
        var biome = new BiomeDefinition(
            "asteria:test/flat",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")]);
        var structure = new StructureDefinition(
            "asteria:detailed", rotation: false, anchor: default,
            voxels: [new StructureVoxelDefinition(
                0, 0, 0, "asteria:sculptable",
                BlockOrientation.Y, detail)],
            restrictions: new StructureRestrictionsDefinition(
                maxSlope: 0, requiresDryGround: false),
            generation: new StructureGenerationDefinition(
                StructureReplacePolicy.Terrain,
                StructureFluidPolicy.Displace, false),
            groundAnchorY: 0);
        var structures = new StructureRegistry([structure]);
        var dyes = new DyeRegistry([
            new DyeDefinition("asteria:red", 0f, 1f, 1f / 3f)
        ]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:moss","texture":"textures/layers/moss.png"}
            """,
            """
            {"id":"asteria:ivy","texture":"textures/layers/ivy.png"}
            """
        ]);
        structures.ValidateBlocks(blocks, dyes, layers);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [biome.Id], 32, 18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            generatedSurfaceStructures: [
                new DimensionGeneratedSurfaceStructureDefinition(
                    biome.Id, structure.Id,
                    spacing: 16, chance: 1f, jitter: 0)
            ]);
        var generator = new BiomeWorldGenerator(
            77UL, dimension, blocks, new FluidRegistry([]),
            new BiomeRegistry([biome]), structures);
        var placement = Assert.Single(generator.SurfaceStructuresIntersecting(
            0, 0, Chunk.Size, Chunk.Size));
        var chunk = generator.Materialize(new ChunkCoord(
            0, placement.AnchorY / Chunk.Size, 0));
        var lx = placement.AnchorX % Chunk.Size;
        var ly = placement.AnchorY % Chunk.Size;
        var lz = placement.AnchorZ % Chunk.Size;
        var cell = chunk.GetCell(lx, ly, lz);
        Assert.Equal(blocks.GetId("asteria:sculptable"), cell.Block);
        Assert.Equal(TextureRotation.Degrees90, cell.TextureRotation);
        Assert.Equal(HorizontalFacing.North, cell.Facing);
        Assert.Equal((ushort)11, cell.State);
        Assert.True(cell.HasMicroblockGeometry);
        Assert.Equal(mask, chunk.GetMicroblockMask(lx, ly, lz));
        Assert.Equal(surface, chunk.GetSurfaceState(lx, ly, lz));
    }
}
