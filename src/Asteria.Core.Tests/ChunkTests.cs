using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkTests
{
    [Fact]
    public void ChunkUsesSixteenCubedRuntimeShape()
    {
        Assert.Equal(16, Chunk.Size);
        Assert.Equal(4096, Chunk.Volume);
    }

    [Fact]
    public void DirectionalFaceMetadataPacksCommonDepthAndPreservesExtendedMasks()
    {
        var compact = new DirectionalFaceVoxelData(
            true,
            1UL,
            2UL,
            4UL,
            8UL,
            16UL,
            64UL,
            new BlockRuntimeId(12));

        Assert.True(compact.IsTerrainVoxel);
        Assert.Equal(new BlockRuntimeId(12), compact.BaseBlock);
        Assert.Equal(1UL, compact.Up);
        Assert.Equal(2UL, compact.NegativeX);
        Assert.Equal(4UL, compact.PositiveX);
        Assert.Equal(8UL, compact.NegativeZ);
        Assert.Equal(16UL, compact.PositiveZ);
        Assert.Equal(64UL, compact.Down);

        // Distance 64 exercises the full-width fallback. Replacing the mask
        // with depth seven must return to the compact representation.
        const ulong extendedMask = (1UL << 63) | (1UL << 6) | 1UL;
        var extended = compact.WithCandidates(
            DirectionalFaceDirection.PositiveZ,
            extendedMask);
        Assert.Equal(extendedMask, extended.PositiveZ);
        Assert.Equal(compact.Up, extended.Up);
        Assert.Equal(compact.Down, extended.Down);

        var withBaseBlock = extended.WithBaseBlock(new BlockRuntimeId(21));
        Assert.Equal(new BlockRuntimeId(21), withBaseBlock.BaseBlock);
        Assert.Equal(extendedMask, withBaseBlock.PositiveZ);

        var compactAgain = withBaseBlock.WithCandidates(
            DirectionalFaceDirection.PositiveZ,
            1UL << 6);
        Assert.Equal(1UL << 6, compactAgain.PositiveZ);
        Assert.Equal(new BlockRuntimeId(21), compactAgain.BaseBlock);
    }

    [Fact]
    public void DirectionalFacePaletteDeduplicatesEquivalentOverflowValues()
    {
        const ulong extendedMask = (1UL << 63) | 0x35UL;
        var first = new DirectionalFaceVoxelData(
            true, 0, extendedMask, 0, 0, 0, 0, BlockRuntimeId.Air);
        var second = new DirectionalFaceVoxelData(
            true, 0, extendedMask, 0, 0, 0, 0, BlockRuntimeId.Air);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        var palette = new PaletteStorage<DirectionalFaceVoxelData>();
        Assert.True(palette.SetInitial(0, first));
        Assert.True(palette.SetInitial(1, second));
        Assert.Equal(1, palette.ActivePaletteEntryCount);
    }

    [Fact]
    public void PaletteStorageCapacityDiagnosticsReportCollectionCapacitiesAndFixedBuffers()
    {
        var palette = new PaletteStorage<DirectionalFaceVoxelData>();
        var empty = palette.CapacityMetrics;

        Assert.Equal(0, empty.OccupiedVoxelCount);
        Assert.Equal(0, empty.PaletteEntryCount);
        Assert.Equal(0, empty.PaletteCapacity);
        Assert.Equal(0, empty.UsageEntryCount);
        Assert.Equal(0, empty.LookupEntryCount);
        Assert.Equal(0, empty.LookupCapacity);
        Assert.Equal(0, empty.FreeSlotCount);
        Assert.Equal(0, empty.FreeSlotCapacity);
        Assert.Equal(Chunk.Volume, empty.IndexCount);
        Assert.Equal((Chunk.Volume + 63) / 64, empty.OccupancyWordCount);
        Assert.Equal(ChunkMeshletMask.Count, empty.MeshletOccupancyCount);

        var first = new DirectionalFaceVoxelData(
            true, 1UL, 0, 0, 0, 0, 0, BlockRuntimeId.Air);
        var second = first.WithBaseBlock(new BlockRuntimeId(12));
        Assert.True(palette.SetInitial(0, first));
        Assert.True(palette.SetInitial(1, first));
        Assert.True(palette.SetInitial(2, second));

        var populated = palette.CapacityMetrics;
        Assert.Equal(3, populated.OccupiedVoxelCount);
        Assert.Equal(2, populated.PaletteEntryCount);
        Assert.Equal(populated.PaletteEntryCount, populated.UsageEntryCount);
        Assert.Equal(populated.PaletteEntryCount, populated.LookupEntryCount);
        Assert.True(populated.PaletteCapacity >= populated.PaletteEntryCount);
        Assert.True(populated.UsageCapacity >= populated.UsageEntryCount);
        Assert.True(populated.LookupCapacity >= populated.LookupEntryCount);
        Assert.Equal(0, populated.FreeSlotCount);
    }

    [Fact]
    public void DirectionalFaceMetadataCloneAndBaseBlockRestorePreserveOverflowMasks()
    {
        const int index = 2 + Chunk.Size * (4 + Chunk.Size * 3);
        const ulong extendedMask = (1UL << 63) | (1UL << 6) | 5UL;
        var baseBlock = new BlockRuntimeId(5);
        var replacementBlock = new BlockRuntimeId(6);
        var metadata = new DirectionalFaceVoxelData(
            true,
            0,
            0,
            extendedMask,
            0,
            0,
            0,
            BlockRuntimeId.Air);

        var chunk = new Chunk();
        Assert.True(chunk.SetBlockAtLinearIndex(index, baseBlock));
        chunk.SetDirectionalFaceDataAtLinearIndex(index, metadata);

        var clone = chunk.CloneForWorker();
        Assert.Equal(metadata, clone.DirectionalFaceDataAtLinearIndex(index));

        Assert.True(clone.SetDirectionalFacePaletteBlockAtLinearIndex(
            index,
            replacementBlock));
        Assert.Equal(replacementBlock, clone.GetBlock(2, 3, 4));
        Assert.Equal(baseBlock, chunk.GetBlock(2, 3, 4));
        Assert.Equal(baseBlock, clone.DirectionalFaceDataAtLinearIndex(index).BaseBlock);
        Assert.Equal(
            extendedMask,
            clone.DirectionalFaceDataAtLinearIndex(index).PositiveX);

        Assert.True(clone.RestoreDirectionalFaceBaseBlockAtLinearIndex(index));
        Assert.Equal(baseBlock, clone.GetBlock(2, 3, 4));
        Assert.Equal(
            extendedMask,
            clone.DirectionalFaceDataAtLinearIndex(index).PositiveX);
        Assert.Equal(metadata, chunk.DirectionalFaceDataAtLinearIndex(index));
    }

    [Fact]
    public void PaletteDeduplicatesCellsAndTracksOccupancy()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);
        var stone = registry.GetId("asteria:stone");
        var dirt = registry.GetId("asteria:dirt");
        var chunk = new Chunk();

        Assert.True(chunk.SetBlock(1, 2, 3, stone));
        Assert.True(chunk.SetBlock(2, 2, 3, stone));
        Assert.True(chunk.SetBlock(3, 2, 3, dirt));

        Assert.Equal(3, chunk.NonEmptyVoxelCount);
        Assert.Equal(2, chunk.PaletteEntryCount);
        Assert.Equal((ulong)3, chunk.Revision);

        Assert.False(chunk.SetBlock(1, 2, 3, stone));
        Assert.Equal((ulong)3, chunk.Revision);

        Assert.True(chunk.SetBlock(3, 2, 3, BlockRuntimeId.Air));
        Assert.Equal(2, chunk.NonEmptyVoxelCount);
        Assert.Equal(1, chunk.PaletteEntryCount);
        Assert.True(chunk.GetCell(3, 2, 3).IsEmpty);
    }

    [Fact]
    public void InitialBlockWritesBuildPaletteAndCommitOneRevision()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);
        var stone = registry.GetId("asteria:stone");
        var dirt = registry.GetId("asteria:dirt");
        var chunk = new Chunk();
        var stoneIndex = 1 + Chunk.Size * 3 + Chunk.Area * 2;
        var dirtIndex = 2 + Chunk.Size * 3 + Chunk.Area * 2;

        using (var initial = chunk.BeginInitialBlockWrites(
            registry.AuthoredCount + 1))
        {
            Assert.True(initial.SetBlockAtLinearIndex(stoneIndex, stone));
            Assert.True(initial.SetBlockAtLinearIndex(dirtIndex, stone));
            Assert.False(initial.SetBlockAtLinearIndex(stoneIndex, stone));
            Assert.Throws<InvalidOperationException>(
                () => initial.SetBlockAtLinearIndex(stoneIndex, dirt));

            Assert.Equal(2, chunk.NonEmptyVoxelCount);
            Assert.Equal(1, chunk.PaletteEntryCount);
            Assert.Equal((ulong)0, chunk.Revision);

            initial.Complete();
            Assert.Equal((ulong)1, chunk.Revision);
            Assert.Throws<ObjectDisposedException>(
                () => initial.SetBlockAtLinearIndex(dirtIndex, dirt));
        }

        Assert.Equal(stone, chunk.GetBlock(1, 2, 3));
        Assert.Equal(stone, chunk.GetBlock(2, 2, 3));
        Assert.True(chunk.SetBlock(3, 2, 3, dirt));
        Assert.Equal((ulong)2, chunk.Revision);
        Assert.Equal(2, chunk.PaletteEntryCount);
    }

    [Fact]
    public void FreshInitialBlockWritesPopulateUniqueSlotsAndDeduplicatePalette()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);
        var stone = registry.GetId("asteria:stone");
        var dirt = registry.GetId("asteria:dirt");
        var chunk = new Chunk();

        using (var initial = chunk.BeginInitialBlockWrites(
            registry.AuthoredCount + 1))
        {
            Assert.True(initial.SetBlockAtLinearIndexFreshUnchecked(17, stone));
            Assert.True(initial.SetBlockAtLinearIndexFreshUnchecked(18, stone));
            Assert.True(initial.SetBlockAtLinearIndexFreshUnchecked(Chunk.Area + 17, dirt));
            Assert.Equal(3, chunk.NonEmptyVoxelCount);
            Assert.Equal(2, chunk.PaletteEntryCount);
            Assert.Equal((ulong)0, chunk.Revision);
        }

        Assert.Equal((ulong)1, chunk.Revision);
        Assert.Equal(stone, chunk.GetCellAtLinearIndexUnchecked(17).Block);
        Assert.Equal(stone, chunk.GetCellAtLinearIndexUnchecked(18).Block);
        Assert.Equal(dirt, chunk.GetCellAtLinearIndexUnchecked(Chunk.Area + 17).Block);
    }

    [Fact]
    public void InitialFluidWritesPreserveOccupiedCellsAndCommitOneRevision()
    {
        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(64, 96, 255),
                opacity: 0.7f),
            new FluidDefinition(
                "asteria:lava",
                new FluidColor(255, 96, 24),
                opacity: 1f),
        ]);
        var water = fluids.GetId("asteria:water");
        var lava = fluids.GetId("asteria:lava");
        var chunk = new Chunk();
        const int existingIndex = 1 + Chunk.Size * 3 + Chunk.Area * 2;
        const int newIndex = 2 + Chunk.Size * 3 + Chunk.Area * 2;

        Assert.True(chunk.SetFluid(1, 2, 3, FluidCell.Source(water)));
        var initialRevision = chunk.Revision;

        using (var initial = chunk.BeginInitialFluidWrites())
        {
            Assert.False(initial.SetFluidAtLinearIndexIfEmpty(
                existingIndex, FluidCell.Source(lava)));
            Assert.True(initial.SetFluidAtLinearIndexIfEmpty(
                newIndex, FluidCell.Source(water)));
            Assert.False(initial.SetFluidAtLinearIndexIfEmpty(
                newIndex, FluidCell.Source(water)));

            Assert.Equal(2, chunk.FluidCount);
            Assert.Equal(initialRevision, chunk.Revision);
            initial.Complete();
            Assert.Equal(initialRevision + 1, chunk.Revision);
            Assert.Throws<ObjectDisposedException>(
                () => initial.IsEmptyAtLinearIndex(newIndex));
        }

        Assert.Equal(water, chunk.GetFluid(1, 2, 3).Fluid);
        Assert.Equal(water, chunk.GetFluid(2, 2, 3).Fluid);
        Assert.True(chunk.SetFluid(3, 2, 3, FluidCell.Source(lava)));
        Assert.Equal(initialRevision + 2, chunk.Revision);
    }

    [Fact]
    public void PaletteReusesReleasedSlotsWithoutGrowingActiveEntries()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
            new BlockDefinition("asteria:clay"),
        ]);
        var chunk = new Chunk();

        Assert.True(
            chunk.SetBlock(
                1,
                1,
                1,
                registry.GetId("asteria:stone")));
        Assert.True(
            chunk.SetBlock(
                2,
                1,
                1,
                registry.GetId("asteria:dirt")));
        Assert.Equal(
            2,
            chunk.PaletteEntryCount);

        Assert.True(
            chunk.SetBlock(
                2,
                1,
                1,
                BlockRuntimeId.Air));
        Assert.Equal(
            1,
            chunk.PaletteEntryCount);

        Assert.True(
            chunk.SetBlock(
                3,
                1,
                1,
                registry.GetId("asteria:clay")));
        Assert.Equal(
            2,
            chunk.PaletteEntryCount);
    }

    [Fact]
    public void OccupiedMeshletMasksTrackBlockAndFluidMutations()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(64, 96, 255),
                opacity: 0.7f),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var water = fluids.GetId("asteria:water");
        var chunk = new Chunk();

        Assert.Equal(
            ChunkMeshletMask.None,
            chunk.OccupiedTerrainMeshlets());
        Assert.Equal(
            ChunkMeshletMask.None,
            chunk.OccupiedFluidMeshlets());

        chunk.SetBlock(7, 7, 7, stone);
        chunk.SetBlock(8, 7, 7, stone);
        chunk.SetBlock(15, 15, 15, stone);
        chunk.SetFluid(0, 8, 8, FluidCell.Source(water));

        Assert.Equal(
            new ChunkMeshletMask((byte)((1 << 0) | (1 << 1) | (1 << 7))),
            chunk.OccupiedTerrainMeshlets());
        Assert.Equal(
            ChunkMeshletMask.Single(6),
            chunk.OccupiedFluidMeshlets());

        chunk.SetBlock(8, 7, 7, BlockRuntimeId.Air);
        chunk.SetBlock(15, 15, 15, BlockRuntimeId.Air);
        chunk.SetFluid(0, 8, 8, FluidCell.Empty);

        Assert.Equal(
            ChunkMeshletMask.Single(0),
            chunk.OccupiedTerrainMeshlets());
        Assert.Equal(
            ChunkMeshletMask.None,
            chunk.OccupiedFluidMeshlets());
        Assert.Equal(
            chunk.OccupiedTerrainMeshlets(),
            chunk.CloneForWorker().OccupiedTerrainMeshlets());
    }

    [Fact]
    public void WorkerClonePreservesPaletteMicroblocksFluidsAndRevision()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(64, 96, 255),
                opacity: 0.7f),
        ]);
        var chunk = new Chunk();
        var stone = blocks.GetId("asteria:stone");
        var water = fluids.GetId("asteria:water");

        Assert.True(
            chunk.SetBlock(
                2,
                3,
                4,
                stone));
        var mask =
            MicroblockMask.Full.Edit(
                0,
                0,
                0,
                MicroblockResolution.Thick,
                occupied: false);
        Assert.True(
            chunk.SetMicroblockMask(
                2,
                3,
                4,
                mask));
        Assert.True(
            chunk.SetFluid(
                5,
                6,
                7,
                FluidCell.Source(water)));

        var clone =
            chunk.CloneForWorker();

        Assert.Equal(
            chunk.Revision,
            clone.Revision);
        Assert.Equal(
            chunk.PaletteEntryCount,
            clone.PaletteEntryCount);
        Assert.Equal(
            chunk.FluidPaletteEntryCount,
            clone.FluidPaletteEntryCount);
        Assert.Equal(
            mask,
            clone.GetMicroblockMask(
                2,
                3,
                4));
        Assert.Equal(
            FluidCell.Source(water),
            clone.GetFluid(
                5,
                6,
                7));

        Assert.True(
            clone.SetBlock(
                8,
                8,
                8,
                stone));
        Assert.True(
            chunk.GetCell(
                8,
                8,
                8).IsEmpty);
    }

    [Fact]
    public void BlockVisitorUsesOccupiedCellsInVoxelOrder()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var chunk = new Chunk();
        var stone =
            blocks.GetId("asteria:stone");

        chunk.SetBlock(
            7,
            12,
            1,
            stone);
        chunk.SetBlock(
            15,
            2,
            9,
            stone);
        chunk.SetBlock(
            1,
            2,
            9,
            stone);

        var visited =
            new List<(int X, int Y, int Z, VoxelCell Cell)>();

        chunk.VisitBlockCells(
            (x, y, z, cell) =>
                visited.Add(
                    (x, y, z, cell)));

        Assert.Equal(
            3,
            visited.Count);
        Assert.Equal(
            (1, 2, 9),
            (
                visited[0].X,
                visited[0].Y,
                visited[0].Z));
        Assert.Equal(
            (15, 2, 9),
            (
                visited[1].X,
                visited[1].Y,
                visited[1].Z));
        Assert.Equal(
            (7, 12, 1),
            (
                visited[2].X,
                visited[2].Y,
                visited[2].Z));
        Assert.All(
            visited,
            entry =>
                Assert.Equal(
                    stone,
                    entry.Cell.Block));
    }

    [Fact]
    public void FluidVisitorUsesOccupiedCellsAndPreservesCoordinates()
    {
        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(64, 96, 255),
                opacity: 0.7f),
        ]);
        var chunk = new Chunk();
        var water = fluids.GetId("asteria:water");

        chunk.SetFluid(
            15,
            2,
            9,
            FluidCell.Source(water));
        chunk.SetFluid(
            1,
            14,
            3,
            FluidCell.Spreading(
                water,
                level: 4,
                spreadDistance: 2));

        var visited =
            new List<(int X, int Y, int Z, FluidCell Fluid)>();

        chunk.VisitFluidCells(
            (x, y, z, fluid) =>
                visited.Add(
                    (x, y, z, fluid)));

        Assert.Equal(2, visited.Count);
        Assert.Contains(
            visited,
            entry =>
                entry.X == 15 &&
                entry.Y == 2 &&
                entry.Z == 9);
        Assert.Contains(
            visited,
            entry =>
                entry.X == 1 &&
                entry.Y == 14 &&
                entry.Z == 3);
    }

    [Fact]
    public void DependencyBoundaryQueriesOnlyInspectRelevantRegion()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        64,
                        96,
                        255),
                    opacity: 0.7f),
            ]);
        var chunk =
            new Chunk();

        Assert.True(
            chunk.SetBlock(
                Chunk.Size - 1,
                4,
                5,
                blocks.GetId(
                    "asteria:stone")));
        Assert.True(
            chunk.SetFluid(
                0,
                6,
                7,
                FluidCell.Source(
                    fluids.GetId(
                        "asteria:water"))));

        Assert.True(
            chunk.HasTerrainContent);
        Assert.True(
            chunk.HasFluidContent);
        Assert.True(
            chunk.DependencyBoundaryHasContent(
                1,
                0,
                0));
        Assert.False(
            chunk.DependencyBoundaryHasFluid(
                1,
                0,
                0));
        Assert.True(
            chunk.DependencyBoundaryHasContent(
                -1,
                0,
                0));
        Assert.True(
            chunk.DependencyBoundaryHasFluid(
                -1,
                0,
                0));
        Assert.False(
            chunk.DependencyBoundaryHasContent(
                0,
                1,
                1));
    }

    [Fact]
    public void AirCellsNormalizeRuntimeState()
    {
        var cell = new VoxelCell(
            BlockRuntimeId.Air,
            TextureRotation.Degrees270,
            BlockOrientation.X,
            HorizontalFacing.North,
            state: 42,
            microblockMaskId: 9);

        Assert.Equal(VoxelCell.Empty, cell);
    }

    [Fact]
    public void RuntimeCellCarriesIndependentOrientationFacingAndTextureRotation()
    {
        var cell = new VoxelCell(new BlockRuntimeId(7))
            .WithOrientation(BlockOrientation.Z)
            .WithFacing(HorizontalFacing.West)
            .WithTextureRotation(TextureRotation.Degrees90);

        Assert.Equal(BlockOrientation.Z, cell.Orientation);
        Assert.Equal(HorizontalFacing.West, cell.Facing);
        Assert.Equal(TextureRotation.Degrees90, cell.TextureRotation);
    }
}
