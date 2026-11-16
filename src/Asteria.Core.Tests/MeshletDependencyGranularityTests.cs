using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MeshletDependencyGranularityTests
{
    [Fact]
    public void ChangesToOneBatchChunkDoNotDiscardUnrelatedMeshlets()
    {
        var world = new VoxelWorld();
        var first = ChunkCoord.Zero;
        var second = new ChunkCoord(4, 0, 0);
        world.InsertChunk(first, new Chunk());
        world.InsertChunk(second, new Chunk());

        var captured = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [first] = ChunkMeshletMask.Single(0),
                [second] = ChunkMeshletMask.Single(0),
            });
        var firstKey = new ChunkMeshletKey(first, 0);
        var secondKey = new ChunkMeshletKey(second, 0);

        Assert.True(captured.Dependencies.IsCurrent(world));
        Assert.True(captured.MeshletDependencies[firstKey].IsCurrent(world));
        Assert.True(captured.MeshletDependencies[secondKey].IsCurrent(world));

        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        world.GetChunk(second).SetBlock(1, 1, 1, blocks.GetId("asteria:stone"));

        Assert.False(captured.Dependencies.IsCurrent(world));
        Assert.True(captured.MeshletDependencies[firstKey].IsCurrent(world));
        Assert.False(captured.MeshletDependencies[secondKey].IsCurrent(world));
    }

    [Fact]
    public void ArrivingDependencyInvalidatesOnlyTouchingMeshlet()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var captured = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [ChunkCoord.Zero] = new ChunkMeshletMask(0b00000011),
            });
        var negativeSide = new ChunkMeshletKey(ChunkCoord.Zero, 0);
        var positiveSide = new ChunkMeshletKey(ChunkCoord.Zero, 1);
        var eastern = new ChunkCoord(1, 0, 0);

        world.InsertChunk(eastern, new Chunk());

        Assert.False(captured.Dependencies.IsCurrent(world));
        Assert.True(captured.MeshletDependencies[negativeSide].IsCurrent(world));
        Assert.False(captured.MeshletDependencies[positiveSide].IsCurrent(world));
    }

    [Fact]
    public void ArchivedAndRestoredSameChunkReusesItsPublishedGeometry()
    {
        var world = new VoxelWorld();
        var center = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        world.InsertChunk(center, new Chunk());
        world.InsertChunk(neighbor, new Chunk());
        var snapshot = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [center] = ChunkMeshletMask.Single(1),
            });
        var dependencies = snapshot.MeshletDependencies[
            new ChunkMeshletKey(center, 1)];

        Assert.True(dependencies.IsCurrent(world));
        world.ArchiveChunk(neighbor);
        world.ArchiveChunk(center);
        world.RestoreChunk(center);
        world.RestoreChunk(neighbor);

        Assert.False(dependencies.IsCurrent(world));
        Assert.True(dependencies.HasUnchangedGeometryAfterRestore(world));
    }

    [Fact]
    public void ContentMutationInvalidatesPreviouslyPublishedGeometryAfterRestore()
    {
        var world = new VoxelWorld();
        var center = ChunkCoord.Zero;
        world.InsertChunk(center, new Chunk());
        var dependencies = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [center] = ChunkMeshletMask.Single(0),
            }).MeshletDependencies[new ChunkMeshletKey(center, 0)];
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);

        world.ArchiveChunk(center);
        world.RestoreChunk(center);
        world.GetChunk(center).SetBlock(
            0, 0, 0, blocks.GetId("asteria:stone"));

        Assert.False(dependencies.HasUnchangedGeometryAfterRestore(world));
    }

    [Fact]
    public void ArchivedNeighborDoesNotInvalidateExistingSpatialGeometry()
    {
        var world = new VoxelWorld();
        var center = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        world.InsertChunk(center, new Chunk());
        world.InsertChunk(neighbor, new Chunk());
        var dependencies = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [center] = ChunkMeshletMask.Single(1),
            }).MeshletDependencies[new ChunkMeshletKey(center, 1)];

        world.ArchiveChunk(center);
        world.ArchiveChunk(neighbor);
        world.RestoreChunk(center);

        // The neighbor is archived, not absent from the generated world.
        // Meshing sees it without making it resident again.
        Assert.True(dependencies.HasUnchangedGeometryAfterRestore(world));
        world.RestoreChunk(neighbor);
        Assert.True(dependencies.HasUnchangedGeometryAfterRestore(world));
    }

    [Fact]
    public void NewNeighborInvalidatesPreviouslyMissingDependency()
    {
        var world = new VoxelWorld();
        var center = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        world.InsertChunk(center, new Chunk());
        var dependencies = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [center] = ChunkMeshletMask.Single(1),
            }).MeshletDependencies[new ChunkMeshletKey(center, 1)];

        Assert.True(dependencies.HasUnchangedGeometryAfterRestore(world));
        world.InsertChunk(neighbor, new Chunk());
        Assert.False(dependencies.HasUnchangedGeometryAfterRestore(world));
    }

    [Fact]
    public void MeshingSnapshotContainsArchivedNeighborBlocksWithoutRestoringThem()
    {
        var world = new VoxelWorld();
        var origin = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var neighborChunk = new Chunk();
        neighborChunk.SetBlock(0, 0, 0, blocks.GetId("asteria:stone"));
        world.InsertChunk(origin, new Chunk());
        world.InsertChunk(neighbor, neighborChunk);
        world.ArchiveChunk(neighbor);

        var snapshot = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [origin] = ChunkMeshletMask.Single(1),
            });
        var dependencies = snapshot.MeshletDependencies[
            new ChunkMeshletKey(origin, 1)];

        Assert.False(world.ContainsChunk(neighbor));
        Assert.True(world.HasArchivedChunk(neighbor));
        Assert.Equal(
            blocks.GetId("asteria:stone"),
            snapshot.World.GetCellOrEmpty(
                new WorldVoxelCoord(Chunk.Size, 0, 0)).Block);
        Assert.True(dependencies.IsCurrent(world));

        world.RestoreChunk(neighbor);
        Assert.False(dependencies.IsCurrent(world));
        Assert.True(dependencies.HasUnchangedGeometryAfterRestore(world));
    }

    [Fact]
    public void MeshingSnapshotCapturedBeforeArchiveHasSameSpatialGeometryAfterArchive()
    {
        var world = new VoxelWorld();
        var origin = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        world.InsertChunk(origin, new Chunk());
        world.InsertChunk(neighbor, new Chunk());
        var captured = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [origin] = ChunkMeshletMask.Single(1),
            });
        var dependencies = captured.MeshletDependencies[
            new ChunkMeshletKey(origin, 1)];

        world.ArchiveChunk(neighbor);
        Assert.False(dependencies.IsCurrent(world));
        Assert.True(dependencies.HasUnchangedGeometryAfterRestore(world));

        var afterArchive = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [origin] = ChunkMeshletMask.Single(1),
            });
        var archivedDependencies = afterArchive.MeshletDependencies[
            new ChunkMeshletKey(origin, 1)];
        Assert.True(archivedDependencies.IsCurrent(world));
        Assert.True(dependencies.HasSameWorldGeometryInputsAs(
            archivedDependencies));
    }
}
