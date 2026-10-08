using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureVoxelStateTests
{
    private static (BlockRegistry Blocks, DyeRegistry Dyes, AttachedLayerRegistry Layers) Registries()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:sculptable", tags: ["fragmentable"],
                secondaryProperties: ["dyed"]),
            new BlockDefinition("asteria:ordinary")
        ]);
        var dyes = new DyeRegistry([new DyeDefinition("asteria:red", 0, 1, 1f / 3f)]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:moss","texture":"textures/layers/moss.png"}
            """,
            """
            {"id":"asteria:ivy","texture":"textures/layers/ivy.png",
             "faces":["top"]}
            """
        ]);
        return (blocks, dyes, layers);
    }

    private static (
        string Json,
        StructureDefinition Definition,
        MicroblockMask Mask,
        BlockSurfaceState Surface) ExportDetailedBlock()
    {
        var (blocks, dyes, layers) = Registries();
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mask = MicroblockMask.Full.Edit(
            0, 0, 0, MicroblockResolution.ExtraThin, occupied: false);
        var surface = new BlockSurfaceState("asteria:red", [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss",
                TextureRotation.Degrees90),
            new AttachedBlockLayer(BlockFace.Front, "asteria:moss"),
            new AttachedBlockLayer(BlockFace.Top, "asteria:ivy")
        ]);
        var p = new WorldVoxelCoord(1, 1, 1);
        var cell = new VoxelCell(blocks.GetId("asteria:sculptable"),
            textureRotation: TextureRotation.Degrees270,
            facing: HorizontalFacing.West, state: 37);
        Assert.True(world.SetBlockStateAt(
            p, new BlockStateSnapshot(cell, mask, surface), out _));
        Assert.True(StructureSelectionBounds.TryCreate(p, p, out var bounds, out _));
        Assert.True(StructureSelectionExporter.TryExport(
            world, blocks, bounds, "asteria:detail_test", out var exported,
            out var error, dyes, layers), error);
        Assert.NotNull(exported);
        var parsed = StructureDefinitionJson.Parse(exported!.Json);
        return (exported.Json, parsed, mask, surface);
    }

    [Fact]
    public void ExportsAndParsesSculptingTintRotationAndOrderedAttachments()
    {
        var (json, definition, mask, surface) = ExportDetailedBlock();
        var voxel = Assert.Single(definition.Voxels);
        var details = Assert.IsType<StructureVoxelState>(voxel.Detail);
        Assert.Equal(TextureRotation.Degrees270, details.TextureRotation);
        Assert.Equal(HorizontalFacing.West, details.Facing);
        Assert.Equal((ushort)37, details.State);
        Assert.Equal(mask, details.Mask);
        Assert.Equal(surface, details.Surface);
        Assert.Equal(new[] { "asteria:moss", "asteria:moss", "asteria:ivy" },
            details.Surface.Layers.Select(x => x.LayerId));
        Assert.Contains("\"microblocks\"", json);
        Assert.Contains("\"dye\"", json);
        var (blocks, dyes, layers) = Registries();
        new StructureRegistry([definition]).ValidateBlocks(blocks, dyes, layers);
    }

    [Fact]
    public void MaskCodecRoundTripsAndRejectsNonPartialOrCorruptMasks()
    {
        var mask = MicroblockMask.Full.Edit(
            7, 7, 7, MicroblockResolution.ExtraThin, false);
        Assert.Equal(mask, MicroblockMask.ParseHexString(mask.ToHexString()));
        Assert.Throws<FormatException>(() => MicroblockMask.ParseHexString("123"));
        Assert.Throws<FormatException>(() => MicroblockMask.ParseHexString(
            new string('z', 128)));
        Assert.Throws<FormatException>(() => MicroblockMask.ParseHexString(
            MicroblockMask.Full.ToHexString()));
        Assert.Throws<FormatException>(() => MicroblockMask.ParseHexString(
            MicroblockMask.Empty.ToHexString()));
    }

    [Fact]
    public void ValidatesRotatingStatefulStructuresAndInvalidAuthoredContent()
    {
        var (_, original, _, _) = ExportDetailedBlock();
        var rotating = new StructureDefinition(
            "asteria:rotating", true, default, original.Voxels);
        Assert.True(rotating.Rotation);
        var (blocks, dyes, layers) = Registries();
        var detail = Assert.IsType<StructureVoxelState>(
            Assert.Single(original.Voxels).Detail);
        var nonSculptable = new StructureDefinition(
            "asteria:invalid_mask_host", false, default, [
                new StructureVoxelDefinition(0, 0, 0, "asteria:ordinary",
                    BlockOrientation.Y, detail)
            ]);
        Assert.Throws<ArgumentException>(() =>
            new StructureRegistry([nonSculptable]).ValidateBlocks(blocks, dyes, layers));
        var badDye = detail with
        {
            Surface = detail.Surface.WithDye("asteria:unknown")
        };
        Assert.Throws<ArgumentException>(() =>
            new StructureRegistry([new StructureDefinition(
                "asteria:invalid_dye", false, default, [
                    new StructureVoxelDefinition(0, 0, 0, "asteria:sculptable",
                        BlockOrientation.Y, badDye)
                ])]).ValidateBlocks(blocks, dyes, layers));
        var badLayer = detail with
        {
            Surface = new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Bottom, "asteria:ivy")
            ])
        };
        Assert.Throws<ArgumentException>(() =>
            new StructureRegistry([new StructureDefinition(
                "asteria:invalid_layer", false, default, [
                    new StructureVoxelDefinition(0, 0, 0, "asteria:sculptable",
                        BlockOrientation.Y, badLayer)
                ])]).ValidateBlocks(blocks, dyes, layers));
    }

    [Fact]
    public void LegacyStructureAndInvalidExtendedFieldsAreHandledStrictly()
    {
        const string template =
            """
            {"id":"asteria:legacy","rotation":false,
             "palette":{"A":{"block":"asteria:ordinary","orientation":"y"}},
             "layers":[{"y":0,"rows":["A"]}]}
            """;
        var old = StructureDefinitionJson.Parse(template);
        Assert.Null(Assert.Single(old.Voxels).Detail);
        var invalid = template.Replace("\"orientation\":\"y\"",
            "\"orientation\":\"y\",\"microblocks\":\"invalid\"");
        Assert.Throws<FormatException>(() => StructureDefinitionJson.Parse(invalid));
        var noBlock = template.Replace(
            "\"block\":\"asteria:ordinary\",\"orientation\":\"y\"",
            "\"clear\":true,\"dye\":\"asteria:red\"");
        Assert.Throws<FormatException>(() => StructureDefinitionJson.Parse(noBlock));
    }
}
