using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldLightingSolverTests
{
    [Fact]
    public void BlockLightPropagatesAcrossChunkBoundary()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:red_lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var lamp = blocks.GetId("asteria:red_lamp");
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        world.SetBlockAt(
            new WorldVoxelCoord(15, 5, 5),
            lamp,
            out _);

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                new WorldVoxelCoord(16, 5, 5)).Red);
    }
    [Fact]
    public void IncrementalColoredEmitterRemovalClearsPropagatedLight()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:lamp",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            15,
                            3,
                            0)),
            ]);
        var lamp =
            blocks.GetId("asteria:lamp");
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var source =
            new WorldVoxelCoord(8, 8, 8);
        var neighbor =
            source + (1, 0, 0);

        Assert.True(
            world.SetBlockAt(
                source,
                lamp,
                out _));

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                neighbor).Red);

        Assert.True(
            world.SetBlockAt(
                source,
                BlockRuntimeId.Air,
                out _));

        VoxelWorldLightingSolver.RelightAfterEdits(
            world,
            blocks,
            new FluidRegistry([]),
            [source]);

        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(
                neighbor).Red);
        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(
                neighbor).Green);
    }

    [Fact]
    public void EqualStrengthRgbSourcesMixByChannelWithoutExtraResetVolume()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:red",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            15,
                            0,
                            0)),
                new BlockDefinition(
                    "asteria:blue",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            0,
                            0,
                            15)),
            ]);
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(6, 8, 8),
                blocks.GetId("asteria:red"),
                out _));
        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(10, 8, 8),
                blocks.GetId("asteria:blue"),
                out _));

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        var mixed =
            world.GetLightOrDark(
                new WorldVoxelCoord(8, 8, 8));

        Assert.Equal((byte)13, mixed.Red);
        Assert.Equal((byte)0, mixed.Green);
        Assert.Equal((byte)13, mixed.Blue);
    }

    [Fact]
    public void IncrementalEmitterPropagatesAcrossChunkBoundary()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:green",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            0,
                            15,
                            0)),
            ]);
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        world.InsertChunk(
            new ChunkCoord(1, 0, 0),
            new Chunk());

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        var source =
            new WorldVoxelCoord(
                Chunk.Size - 1,
                8,
                8);

        Assert.True(
            world.SetBlockAt(
                source,
                blocks.GetId("asteria:green"),
                out _));

        VoxelWorldLightingSolver.RelightAfterEdits(
            world,
            blocks,
            new FluidRegistry([]),
            [source]);

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                new WorldVoxelCoord(
                    Chunk.Size,
                    8,
                    8)).Green);
    }

    [Fact]
    public void IncrementalEmitterRemovalClearsLightAcrossChunkSeam()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:lamp",
                lightDampening: 15,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var source = new WorldVoxelCoord(Chunk.Size - 1, 8, 0);
        var acrossSeam = new WorldVoxelCoord(Chunk.Size, 8, 0);
        var inChunk = new WorldVoxelCoord(Chunk.Size - 2, 8, 0);
        Assert.True(world.SetBlockAt(
            source, blocks.GetId("asteria:lamp"), out _));
        VoxelWorldLightingSolver.Initialize(
            world, blocks, new FluidRegistry([]));
        Assert.Equal((byte)14, world.GetLightOrDark(acrossSeam).Red);
        Assert.Equal((byte)14, world.GetLightOrDark(inChunk).Red);

        Assert.True(world.SetBlockAt(
            source, BlockRuntimeId.Air, out _));
        VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, new FluidRegistry([]), [source]);

        Assert.Equal((byte)0, world.GetLightOrDark(acrossSeam).Red);
        Assert.Equal((byte)0, world.GetLightOrDark(inChunk).Red);
        Assert.Equal((byte)15, world.GetLightOrDark(acrossSeam).Sky);
    }

    [Fact]
    public void IncrementalDirectSkyTreatsUnloadedVerticalGapAsOpenSky()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone",
                    lightDampening: 15),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var world = new VoxelWorld();
        var lower =
            ChunkCoord.Zero;
        var upper =
            new ChunkCoord(0, 2, 0);

        world.InsertChunk(
            lower,
            new Chunk());
        world.InsertChunk(
            upper,
            new Chunk());

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(
                    8,
                    2 * Chunk.Size,
                    8),
                stone,
                out _));

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        var lowerPosition =
            new WorldVoxelCoord(
                8,
                Chunk.Size - 1,
                8);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(
                lowerPosition).Sky);

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(7, 8, 8),
                stone,
                out _));

        VoxelWorldLightingSolver.RelightAfterEdits(
            world,
            blocks,
            new FluidRegistry([]),
            [new WorldVoxelCoord(7, 8, 8)]);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(
                lowerPosition).Sky);
    }

}


public sealed class LightingResultIntegratorTests
{
    [Fact]
    public void MultipleLightChangesInOneMeshletBumpRevisionOnce()
    {
        var world = new VoxelWorld();
        var chunk =
            new Chunk();
        Assert.True(
            chunk.SetBlock(
                2,
                2,
                2,
                new BlockRuntimeId(1)));
        Assert.True(
            chunk.SetFluid(
                4,
                2,
                2,
                FluidCell.Source(
                    new FluidRuntimeId(1))));
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);
        var snapshot =
            world.CloneForWorker();

        Assert.True(
            snapshot.TrySetLight(
                new WorldVoxelCoord(2, 2, 2),
                new VoxelLight(15, 4, 0, 0)));
        Assert.True(
            snapshot.TrySetLight(
                new WorldVoxelCoord(3, 2, 2),
                new VoxelLight(15, 3, 0, 0)));

        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
        var worldUpdates =
            new WorldUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var integrator =
            new LightingResultIntegrator(
                world,
                worldUpdates,
                fluidMeshUpdates);

        var result =
            integrator.Apply(
                snapshot,
                [
                    new WorldVoxelCoord(2, 2, 2),
                    new WorldVoxelCoord(3, 2, 2),
                ]);

        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);

        Assert.Equal(2, result.ChangedVoxelCount);
        Assert.Equal(1, result.DirtyChunkCount);
        Assert.Equal(1, result.DirtyMeshletCount);
        Assert.Equal(
            0UL,
            terrainRevisions.Get(key));
        Assert.Equal(
            0UL,
            fluidRevisions.Get(key));
        Assert.Equal(
            (byte)4,
            world.GetLightOrDark(
                new WorldVoxelCoord(2, 2, 2)).Red);
        Assert.True(worldUpdates.HasMeshWork);
        Assert.True(fluidMeshUpdates.HasWork);
    }

    [Fact]
    public void LightingRefreshDoesNotInvalidateStructuralMeshRevision()
    {
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var revisions =
            new MeshletContentRevisions();
        var position =
            new WorldVoxelCoord(
                2,
                2,
                2);

        revisions.BumpVoxelEdit(
            world,
            position);
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);
        var captured =
            revisions.Get(key);
        var snapshot =
            world.CloneForWorker();

        Assert.True(
            snapshot.TrySetLight(
                position,
                new VoxelLight(
                    15,
                    5,
                    0,
                    0)));

        var integrator =
            new LightingResultIntegrator(
                world,
                new WorldUpdateQueue(),
                new FluidMeshUpdateQueue());

        integrator.Apply(
            snapshot,
            [position]);

        Assert.True(
            revisions.IsCurrent(
                key,
                captured));
    }

    [Fact]
    public void BoundaryLightChangeInvalidatesBothLoadedChunkHalos()
    {
        var world = new VoxelWorld();
        var left =
            new Chunk();
        var right =
            new Chunk();

        Assert.True(
            left.SetBlock(
                Chunk.Size - 1,
                4,
                4,
                new BlockRuntimeId(1)));
        Assert.True(
            left.SetFluid(
                Chunk.Size - 1,
                6,
                6,
                FluidCell.Source(
                    new FluidRuntimeId(1))));
        Assert.True(
            right.SetBlock(
                0,
                4,
                4,
                new BlockRuntimeId(1)));
        Assert.True(
            right.SetFluid(
                0,
                6,
                6,
                FluidCell.Source(
                    new FluidRuntimeId(1))));

        world.InsertChunk(
            ChunkCoord.Zero,
            left);
        world.InsertChunk(
            new ChunkCoord(1, 0, 0),
            right);
        var snapshot =
            world.CloneForWorker();
        var position =
            new WorldVoxelCoord(
                Chunk.Size - 1,
                4,
                4);

        Assert.True(
            snapshot.TrySetLight(
                position,
                new VoxelLight(
                    15,
                    0,
                    10,
                    0)));

        var worldUpdates =
            new WorldUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var integrator =
            new LightingResultIntegrator(
                world,
                worldUpdates,
                fluidMeshUpdates);

        var result =
            integrator.Apply(
                snapshot,
                [position]);

        Assert.Equal(2, result.DirtyChunkCount);

        var terrain =
            worldUpdates.DrainMeshlets();
        var fluid =
            fluidMeshUpdates.Drain();

        Assert.Contains(
            ChunkCoord.Zero,
            terrain.DirtyMeshlets.Keys);
        Assert.Contains(
            new ChunkCoord(1, 0, 0),
            terrain.DirtyMeshlets.Keys);
        Assert.Contains(
            ChunkCoord.Zero,
            fluid.DirtyMeshlets.Keys);
        Assert.Contains(
            new ChunkCoord(1, 0, 0),
            fluid.DirtyMeshlets.Keys);
    }
}


public sealed class VoxelLightingSnapshotTests
{
    [Fact]
    public void SnapshotScopesContentToNearbyHorizontalColumns()
    {
        var world = new VoxelWorld();
        var center = new ChunkCoord(0, 0, 0);
        var above = new ChunkCoord(0, 1, 0);
        var neighbor = new ChunkCoord(1, 0, 0);
        var far = new ChunkCoord(3, 0, 0);

        foreach (var coord in new[] { center, above, neighbor, far })
        {
            world.InsertChunk(coord, new Chunk());
        }

        var snapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        Assert.True(snapshot.World.ContainsChunk(center));
        Assert.True(snapshot.World.ContainsChunk(above));
        Assert.True(snapshot.World.ContainsChunk(neighbor));
        Assert.False(snapshot.World.ContainsChunk(far));
    }

    [Fact]
    public void UnrelatedContentEditDoesNotInvalidateLightingSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(3, 0, 0), new Chunk());

        var snapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(3 * Chunk.Size + 1, 3, 3),
            new BlockRuntimeId(1),
            out _));

        Assert.True(snapshot.Dependencies.IsCurrent(world));
    }

    [Fact]
    public void RelevantContentOrColumnResidencyInvalidatesLightingSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var contentSnapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(4, 3, 3),
            new BlockRuntimeId(1),
            out _));
        Assert.False(contentSnapshot.Dependencies.IsCurrent(world));

        var residencySnapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());

        Assert.False(residencySnapshot.Dependencies.IsCurrent(world));
    }
}
