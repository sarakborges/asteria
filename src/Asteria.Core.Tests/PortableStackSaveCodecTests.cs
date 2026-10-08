using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PortableStackSaveCodecTests
{
    private static byte[] Encode(
        InventoryStack? stack, BlockRegistry blocks,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            PortableStackSaveCodec.Write(writer, stack, blocks, dyes, layers);
        return stream.ToArray();
    }

    private static InventoryStack? Decode(
        byte[] bytes, BlockRegistry blocks, DyeRegistry dyes,
        AttachedLayerRegistry layers)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        var item = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
        Assert.Equal(stream.Length, stream.Position);
        return item;
    }

    [Fact]
    public void PortableBlockRemapsAcrossDifferentBlockRegistryOrders()
    {
        var oldBlocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:granite")]);
        var newBlocks = new BlockRegistry([
            new BlockDefinition("asteria:granite"),
            new BlockDefinition("asteria:stone")]);
        var dyes = new DyeRegistry([]);
        var layers = new AttachedLayerRegistry([]);
        var cell = new VoxelCell(oldBlocks.GetId("asteria:granite"),
            orientation: BlockOrientation.X,
            facing: HorizontalFacing.West, state: 23);
        var portable = new InventoryStack(InventoryEntry.FromBlock(
            "asteria:granite", BlockStateSnapshot.FromCell(cell)), 12);
        var saved = Encode(portable, oldBlocks, dyes, layers);
        var restored = Decode(saved, newBlocks, dyes, layers);

        Assert.Equal("asteria:granite", restored!.Id);
        Assert.Equal(12, restored.Quantity);
        Assert.Equal(newBlocks.GetId("asteria:granite"), restored.Block!.Cell.Block);
        Assert.Equal(BlockOrientation.X, restored.Block.Cell.Orientation);
        Assert.Equal(HorizontalFacing.West, restored.Block.Cell.Facing);
        Assert.Equal((ushort)23, restored.Block.Cell.State);
        Assert.Equal(saved, Encode(restored, newBlocks, dyes, layers));
    }

    [Fact]
    public void ItemToolAndNullStacksPreserveMetadataAndKind()
    {
        var blocks = new BlockRegistry([]);
        var dyes = new DyeRegistry([]);
        var layers = new AttachedLayerRegistry([]);
        var metadata = new Dictionary<string, string> {
            ["destination"] = "asteria:umbral", ["wear"] = "7"
        };
        var item = new InventoryStack(
            InventoryEntry.FromItem("asteria:map", metadata, maxStackSize: 16), 9);
        var tool = new InventoryStack(InventoryEntry.FromTool(
            "asteria:slicer", metadata));

        Assert.Equal(item, Decode(Encode(item, blocks, dyes, layers),
            blocks, dyes, layers));
        Assert.Equal(tool, Decode(Encode(tool, blocks, dyes, layers),
            blocks, dyes, layers));
        Assert.Null(Decode(Encode(null, blocks, dyes, layers),
            blocks, dyes, layers));
        metadata["destination"] = "changed";
        Assert.Equal("asteria:umbral", item.Entry.Metadata["destination"]);
    }

    [Fact]
    public void CorruptedOrUnknownBlockStacksAreRejected()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var dyes = new DyeRegistry([]);
        var layers = new AttachedLayerRegistry([]);
        var stack = new InventoryStack(InventoryEntry.FromBlock("asteria:stone",
            BlockStateSnapshot.FromCell(
                new VoxelCell(blocks.GetId("asteria:stone")))));
        var saved = Encode(stack, blocks, dyes, layers);

        Assert.Throws<InvalidDataException>(() =>
            Decode(saved, new BlockRegistry([]), dyes, layers));
        var malformed = (byte[])saved.Clone();
        malformed[0] = 2;
        Assert.Throws<InvalidDataException>(() => Decode(malformed, blocks, dyes, layers));
        Assert.Throws<InvalidDataException>(() =>
            Decode(saved[..^1], blocks, dyes, layers));
    }
}
