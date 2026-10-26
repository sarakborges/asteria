using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkMeshletMaskTests
{
    [Fact]
    public void ChunkTracksTerrainAndFluidOccupancyPerMeshletAcrossMutationsAndClones()
    {
        var chunk = new Chunk();
        var stone = new BlockRuntimeId(1);
        var dirt = new BlockRuntimeId(2);
        var water = new FluidRuntimeId(1);

        chunk.SetBlock(1, 1, 1, stone);   // Meshlet 0.
        chunk.SetBlock(9, 9, 9, stone);   // Meshlet 7.
        chunk.SetBlock(10, 9, 9, dirt);   // Meshlet 7.
        chunk.SetFluid(2, 2, 2, FluidCell.Source(water));
        chunk.SetFluid(9, 10, 9, FluidCell.Source(water));

        Assert.Equal(1, chunk.TerrainVoxelCountInMeshlet(0));
        Assert.Equal(2, chunk.TerrainVoxelCountInMeshlet(7));
        Assert.Equal(1, chunk.FluidVoxelCountInMeshlet(0));
        Assert.Equal(1, chunk.FluidVoxelCountInMeshlet(7));
        Assert.True(chunk.OccupiedTerrainMeshlets().ContainsIndex(7));
        Assert.True(chunk.OccupiedFluidMeshlets().ContainsIndex(7));

        chunk.SetBlock(1, 1, 1, BlockRuntimeId.Air);
        chunk.SetFluid(2, 2, 2, FluidCell.Empty);
        Assert.Equal(0, chunk.TerrainVoxelCountInMeshlet(0));
        Assert.Equal(0, chunk.FluidVoxelCountInMeshlet(0));
        Assert.False(chunk.OccupiedTerrainMeshlets().ContainsIndex(0));
        Assert.False(chunk.OccupiedFluidMeshlets().ContainsIndex(0));

        // Replacing one occupied value with another does not change counts.
        chunk.SetBlock(9, 9, 9, dirt);
        Assert.Equal(2, chunk.TerrainVoxelCountInMeshlet(7));

        var clone = chunk.CloneForWorker();
        clone.SetBlock(1, 1, 1, stone);
        clone.SetFluid(2, 2, 2, FluidCell.Source(water));
        Assert.Equal(0, chunk.TerrainVoxelCountInMeshlet(0));
        Assert.Equal(0, chunk.FluidVoxelCountInMeshlet(0));
        Assert.Equal(1, clone.TerrainVoxelCountInMeshlet(0));
        Assert.Equal(1, clone.FluidVoxelCountInMeshlet(0));
        Assert.True(clone.OccupiedTerrainMeshlets().ContainsIndex(0));
        Assert.True(clone.OccupiedFluidMeshlets().ContainsIndex(0));
    }

    [Fact]
    public void InteriorEditTouchesOnlyItsMeshlet()
    {
        var mask = ChunkMeshletMask.ForWorldPosition(
            new ChunkCoord(0, 0, 0),
            new WorldVoxelCoord(3, 3, 3));

        Assert.Equal(1, mask.SelectedCount);
        Assert.True(mask.ContainsIndex(0));
    }

    [Fact]
    public void EditOnInternalMeshletBoundaryTouchesBothSides()
    {
        var mask = ChunkMeshletMask.ForWorldPosition(
            new ChunkCoord(0, 0, 0),
            new WorldVoxelCoord(7, 3, 3));

        Assert.Equal(2, mask.SelectedCount);
        Assert.True(mask.ContainsIndex(0));
        Assert.True(mask.ContainsIndex(1));
    }

    [Fact]
    public void WorldChunkBoundaryIncludesNeighborHalo()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var queue = new WorldUpdateQueue();

        queue.EnqueueVoxelEdit(
            world,
            new WorldVoxelCoord(15, 2, 2));

        var batch = queue.DrainMeshlets();

        Assert.Contains(
            new ChunkCoord(0, 0, 0),
            batch.DirtyMeshlets.Keys);
        Assert.Contains(
            new ChunkCoord(1, 0, 0),
            batch.DirtyMeshlets.Keys);
    }
}

public sealed class ChunkMeshletDependencyMaskTests
{
    [Fact]
    public void PositiveXDependencySelectsPositiveXMeshlets()
    {
        var mask = ChunkMeshletMask.ForDependencyOffset(1, 0, 0);

        Assert.Equal(4, mask.SelectedCount);
        Assert.True(mask.ContainsIndex(1));
        Assert.True(mask.ContainsIndex(3));
        Assert.True(mask.ContainsIndex(5));
        Assert.True(mask.ContainsIndex(7));
    }

    [Fact]
    public void CornerDependencySelectsSingleCornerMeshlet()
    {
        var mask = ChunkMeshletMask.ForDependencyOffset(-1, 1, -1);

        Assert.Equal(1, mask.SelectedCount);
        Assert.True(mask.ContainsIndex(4));
    }
}
