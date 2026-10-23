using System.Numerics;
using System.Security.Cryptography;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectOccupancyTests
{
    private static readonly BlockRegistry Blocks = new([
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:dirt"),
    ]);
    private static readonly FluidRegistry Fluids = new([]);
    private static readonly DyeRegistry Dyes = new([]);
    private static readonly AttachedLayerRegistry Layers = new([]);

    [Fact]
    public void ObjectsAttachIndependentlyAndAreBoundedPerSupport()
    {
        var chunk = new Chunk();
        var grass = new ObjectCell("asteria:grass", BlockFace.Top);
        Assert.False(chunk.AddObject(1, 2, 3, grass));
        Assert.True(chunk.SetBlock(1, 2, 3, Blocks.GetId("asteria:stone")));
        Assert.True(chunk.AddObject(1, 2, 3, grass));
        Assert.False(chunk.AddObject(1, 2, 3, grass));
        Assert.Equal(1, chunk.NonEmptyVoxelCount);
        Assert.Equal(1, chunk.ObjectCount);
        Assert.Equal((ulong)1, chunk.ObjectRevision);
        for (var i = 1; i < Chunk.MaximumObjectsPerSupport; i++)
        {
            var obj = new ObjectCell($"asteria:grass_{i}", BlockFace.Top);
            Assert.True(chunk.AddObject(1, 2, 3, obj));
        }
        Assert.False(chunk.AddObject(1, 2, 3,
            new ObjectCell("asteria:extra", BlockFace.Top)));
        Assert.Equal(Chunk.MaximumObjectsPerSupport, chunk.ObjectCount);
        Assert.False(chunk.RemoveObject(0, 0, 0, grass));
        Assert.True(chunk.RemoveObject(1, 2, 3, grass));
        Assert.Equal(Chunk.MaximumObjectsPerSupport - 1, chunk.ObjectCount);
    }

    [Fact]
    public void SupportRemovalDetachesObjectsWithoutTouchingOtherVoxels()
    {
        var chunk = new Chunk();
        chunk.SetBlock(1, 2, 3, Blocks.GetId("asteria:stone"));
        chunk.SetBlock(2, 2, 3, Blocks.GetId("asteria:dirt"));
        var flower = new ObjectCell("asteria:flower", BlockFace.Top);
        chunk.AddObject(1, 2, 3, flower);
        chunk.AddObject(2, 2, 3, flower);
        var rev = chunk.ObjectRevision;
        Assert.True(chunk.SetBlock(1, 2, 3, BlockRuntimeId.Air));
        Assert.Empty(chunk.ObjectsAt(1, 2, 3));
        Assert.Equal(new[] { flower }, chunk.ObjectsAt(2, 2, 3));
        Assert.Equal(1, chunk.ObjectCount);
        Assert.True(chunk.ObjectRevision > rev);
        var cloned = chunk.CloneForWorker();
        Assert.Equal(new[] { flower }, cloned.ObjectsAt(2, 2, 3));
        Assert.Equal(chunk.ObjectRevision, cloned.ObjectRevision);
        Assert.True(cloned.RemoveObject(2, 2, 3, flower));
        Assert.Single(chunk.ObjectsAt(2, 2, 3));
    }

    [Fact]
    public void WorldMutationTracksDirtyChunksAndPreservesBlockOccupancy()
    {
        var world = new VoxelWorld();
        var support = new WorldVoxelCoord(1, 2, 3);
        var obj = new ObjectCell("asteria:kelp", BlockFace.Top,
            TextureRotation.Degrees90);
        Assert.False(world.AddObjectAt(support, obj));
        var chunk = new Chunk();
        chunk.SetBlock(1, 2, 3, Blocks.GetId("asteria:stone"));
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var revision = world.Revision;
        Assert.True(world.AddObjectAt(support, obj));
        Assert.False(world.AddObjectAt(support, obj));
        Assert.True(world.Revision > revision);
        Assert.Equal(Blocks.GetId("asteria:stone"), world.GetCellOrEmpty(support).Block);
        Assert.True(world.TryGetObjects(support, out var objects));
        Assert.Equal(new[] { obj }, objects);
        Assert.True(world.RemoveObjectAt(support, obj));
        Assert.True(world.TryGetObjects(support, out objects));
        Assert.Empty(objects);
        Assert.True(world.DirtyChunkCount > 0);
        Assert.False(world.AddObjectAt(new WorldVoxelCoord(1, -1, 3), obj));
    }

    [Fact]
    public void ObjectTransformIsQuantizedAndRejectsInvalidValues()
    {
        var encoded = ObjectTransform.FromParts(
            new Vector3(0.125f, -0.25f, 0.375f),
            new Vector3(1f, 0.5f, 2f));
        Assert.Equal(new Vector3(0.125f, -0.25f, 0.375f), encoded.Offset);
        Assert.Equal(new Vector3(1f, 0.5f, 2f), encoded.Scale);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObjectTransform.FromParts(Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObjectTransform.FromParts(new Vector3(9f, 0f, 0f), Vector3.One));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ObjectTransform(0, 0, 0, 0, 1024, 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ObjectCell("asteria:grass", BlockFace.Top,
                transform: default(ObjectTransform)));
    }

    [Fact]
    public void ChunkCodecRoundTripsObjectsDeterministicallyAndKeepsLegacyV2()
    {
        var chunk = new Chunk();
        chunk.SetBlock(1, 2, 3, Blocks.GetId("asteria:stone"));
        chunk.SetBlock(2, 2, 3, Blocks.GetId("asteria:dirt"));
        var first = new ObjectCell("asteria:flower", BlockFace.Top,
            TextureRotation.Degrees90,
            ObjectTransform.FromParts(new Vector3(0.125f, 0f, -0.25f), Vector3.One));
        var second = new ObjectCell("asteria:moss", BlockFace.Left);
        // Insertion order is not the persistence order.
        Assert.True(chunk.AddObject(1, 2, 3, second));
        Assert.True(chunk.AddObject(1, 2, 3, first));
        var encoded = ChunkSaveCodec.Encode(chunk, Blocks, Fluids, Dyes, Layers);
        var decoded = ChunkSaveCodec.Decode(encoded, Blocks, Fluids, Dyes, Layers);
        Assert.Equal(new[] { second, first }, decoded.ObjectsAt(1, 2, 3).OrderBy(o =>
            o.Id == "asteria:moss" ? 0 : 1));
        Assert.Equal(2, decoded.ObjectCount);
        Assert.Equal(encoded, ChunkSaveCodec.Encode(decoded, Blocks, Fluids, Dyes, Layers));

        var other = new Chunk();
        other.SetBlock(1, 2, 3, Blocks.GetId("asteria:stone"));
        other.SetBlock(2, 2, 3, Blocks.GetId("asteria:dirt"));
        other.AddObject(1, 2, 3, first);
        other.AddObject(1, 2, 3, second);
        Assert.Equal(encoded, ChunkSaveCodec.Encode(other, Blocks, Fluids, Dyes, Layers));

        var emptyObjects = new Chunk();
        emptyObjects.SetBlock(0, 0, 0, Blocks.GetId("asteria:stone"));
        var currentFormat = ChunkSaveCodec.Encode(emptyObjects, Blocks, Fluids, Dyes, Layers);
        // Legacy v2 has neither the player-placement provenance byte nor the final Objects group-count field.
        var legacyPayload = currentFormat[..^(32 + 4)];
        using (var legacyStream = new MemoryStream(legacyPayload, writable: false))
        using (var legacyReader = new BinaryReader(legacyStream))
        {
            _ = legacyReader.ReadUInt32();
            _ = legacyReader.ReadUInt16();
            Assert.Equal(1, legacyReader.ReadInt32());
            _ = legacyReader.ReadUInt16();
            _ = legacyReader.ReadString();
            _ = legacyReader.ReadByte();
            _ = legacyReader.ReadByte();
            _ = legacyReader.ReadByte();
            _ = legacyReader.ReadUInt16();
            var provenanceOffset = checked((int)legacyStream.Position);
            var v2Payload = new byte[legacyPayload.Length - 1];
            Array.Copy(legacyPayload, 0, v2Payload, 0, provenanceOffset);
            Array.Copy(legacyPayload, provenanceOffset + 1, v2Payload,
                provenanceOffset, legacyPayload.Length - provenanceOffset - 1);
            v2Payload[4] = 2;
            v2Payload[5] = 0;
            var v2 = v2Payload.Concat(SHA256.HashData(v2Payload)).ToArray();
            var restoredV2 = ChunkSaveCodec.Decode(v2, Blocks, Fluids, Dyes, Layers);
            Assert.Empty(restoredV2.ObjectsAt(0, 0, 0));
            Assert.Equal(emptyObjects.GetCell(0, 0, 0), restoredV2.GetCell(0, 0, 0));
        }
    }

    [Fact]
    public void InvalidEncodedTransformIsRejectedEvenWithValidChecksum()
    {
        var chunk = new Chunk();
        chunk.SetBlock(0, 0, 0, Blocks.GetId("asteria:stone"));
        chunk.AddObject(0, 0, 0,
            new ObjectCell("asteria:flower", BlockFace.Top));
        var bytes = ChunkSaveCodec.Encode(chunk, Blocks, Fluids, Dyes, Layers);
        // Last 6 bytes of the payload are three UInt16 scales.
        bytes[^38] = 0;
        bytes[^37] = 0;
        var digest = SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32));
        digest.CopyTo(bytes, bytes.Length - 32);
        Assert.Throws<InvalidDataException>(() =>
            ChunkSaveCodec.Decode(bytes, Blocks, Fluids, Dyes, Layers));
    }
}
