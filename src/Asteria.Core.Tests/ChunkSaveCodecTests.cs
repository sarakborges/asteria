using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkSaveCodecTests
{
    private static (
        BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content(bool reverse = false)
    {
        var blockDefinitions = new[]
        {
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:basalt"),
        };
        var fluidDefinitions = new[]
        {
            new FluidDefinition("asteria:water", new FluidColor(30, 70, 240), 0.5f),
            new FluidDefinition("asteria:lava", new FluidColor(255, 100, 20), 1f),
        };
        return (
            new BlockRegistry(reverse ? blockDefinitions.Reverse() : blockDefinitions),
            new FluidRegistry(reverse ? fluidDefinitions.Reverse() : fluidDefinitions),
            new DyeRegistry([new DyeDefinition("asteria:red", 0, 0.8f, 0.5f)]),
            new AttachedLayerRegistry([
                new AttachedLayerDefinition("asteria:moss", "textures/moss.png",
                    [BlockFace.Top]),
            ]));
    }

    private static byte[] Encode(
        Chunk chunk,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        ChunkSaveCodec.Encode(chunk,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    private static Chunk Decode(
        byte[] bytes,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        ChunkSaveCodec.Decode(bytes,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    [Fact]
    public void EmptyChunksRoundTripDeterministically()
    {
        var content = Content();
        var original = new Chunk();
        var bytes = Encode(original, content);
        var loaded = Decode(bytes, content);
        Assert.True(loaded.IsEmpty);
        Assert.Equal(bytes, Encode(loaded, content));
    }

    [Fact]
    public void RoundTripsBlocksFluidsMicroblocksDyesAndOrderedAttachments()
    {
        var source = Content();
        var chunk = new Chunk();
        var originalCell = new VoxelCell(
            source.Blocks.GetId("asteria:stone"),
            TextureRotation.Degrees90,
            BlockOrientation.X,
            HorizontalFacing.West,
            state: 213);
        Assert.True(chunk.SetCell(2, 3, 4, originalCell));
        var mask = MicroblockMask.Full.Edit(
            0, 0, 0, MicroblockResolution.Thick, occupied: false);
        Assert.True(chunk.SetMicroblockMask(2, 3, 4, mask));
        var surface = new BlockSurfaceState("asteria:red",
        [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss",
                TextureRotation.Degrees180),
        ]);
        Assert.True(chunk.SetSurfaceState(2, 3, 4, surface));

        Assert.True(chunk.SetBlock(15, 15, 15,
            source.Blocks.GetId("asteria:basalt")));
        Assert.True(chunk.SetFluid(1, 2, 3,
            FluidCell.Spreading(source.Fluids.GetId("asteria:water"), 4, 3)));
        Assert.True(chunk.SetFluid(15, 0, 0,
            FluidCell.Source(source.Fluids.GetId("asteria:lava"))));

        var encoded = Encode(chunk, source);
        var target = Content(reverse: true);
        var restored = Decode(encoded, target);

        var restoredCell = restored.GetCell(2, 3, 4);
        Assert.Equal(target.Blocks.GetId("asteria:stone"), restoredCell.Block);
        Assert.Equal(originalCell.TextureRotation, restoredCell.TextureRotation);
        Assert.Equal(originalCell.Orientation, restoredCell.Orientation);
        Assert.Equal(originalCell.Facing, restoredCell.Facing);
        Assert.Equal(originalCell.State, restoredCell.State);
        Assert.Equal(mask, restored.GetMicroblockMask(2, 3, 4));
        Assert.Equal(surface, restored.GetSurfaceState(2, 3, 4));
        Assert.Equal(target.Blocks.GetId("asteria:basalt"),
            restored.GetBlock(15, 15, 15));
        Assert.Equal(FluidCell.Spreading(target.Fluids.GetId("asteria:water"), 4, 3),
            restored.GetFluid(1, 2, 3));
        Assert.Equal(FluidCell.Source(target.Fluids.GetId("asteria:lava")),
            restored.GetFluid(15, 0, 0));
        Assert.Equal(encoded, Encode(restored, target));
    }

    [Fact]
    public void RejectsTruncationCorruptionAndMissingContent()
    {
        var source = Content();
        var chunk = new Chunk();
        chunk.SetBlock(4, 5, 6, source.Blocks.GetId("asteria:stone"));
        var encoded = Encode(chunk, source);

        Assert.Throws<InvalidDataException>(() =>
            Decode(encoded[..^4], source));
        var corrupted = (byte[])encoded.Clone();
        corrupted[8] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => Decode(corrupted, source));

        var unknownBlocks = new BlockRegistry([
            new BlockDefinition("asteria:basalt"),
        ]);
        Assert.Throws<InvalidDataException>(() => ChunkSaveCodec.Decode(
            encoded, unknownBlocks, source.Fluids, source.Dyes, source.Layers));
    }

    [Fact]
    public void RejectsCoOccupiedFluidAndSolidDuringSerialization()
    {
        var content = Content();
        var chunk = new Chunk();
        chunk.SetBlock(3, 2, 1, content.Blocks.GetId("asteria:stone"));
        chunk.SetFluid(3, 2, 1, FluidCell.Source(content.Fluids.GetId("asteria:water")));
        Assert.Throws<InvalidOperationException>(() => Encode(chunk, content));
    }

    [Fact]
    public void DecodingDoesNotMutateLiveWorldOrRegisteredChunk()
    {
        var content = Content();
        var world = new VoxelWorld();
        var address = new ChunkCoord(0, 0, 0);
        var existing = new Chunk();
        existing.SetBlock(1, 2, 3, content.Blocks.GetId("asteria:basalt"));
        world.InsertChunk(address, existing);
        var broken = Encode(existing, content);
        broken[^1] ^= 0x01;

        Assert.Throws<InvalidDataException>(() => Decode(broken, content));
        Assert.Same(existing, world.GetChunk(address));
        Assert.Equal(content.Blocks.GetId("asteria:basalt"),
            world.GetChunk(address).GetBlock(1, 2, 3));
    }
}
