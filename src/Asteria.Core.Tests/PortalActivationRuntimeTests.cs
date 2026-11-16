using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PortalActivationRuntimeTests
{
    private static (PortalActivationRuntime Portal, VoxelWorld World,
        VoxelMutationRuntime Mutations, BlockRegistry Blocks) Fixture(
        int maxX = Chunk.Size - 1, int maxY = Chunk.Size - 1)
    {
        var frame = new BlockDefinition("asteria:glass",
            tags: ["is_portal_frame",
                "accept_dimension:asteria:umbral",
                "accept_dimension:asteria:overworld"]);
        var portal = new BlockDefinition("asteria:portal",
            shape: BlockShapeDefinition.CenteredLayer(0.10f),
            orientations: [BlockOrientation.X, BlockOrientation.Y, BlockOrientation.Z],
            isCollidable: false, lightDampening: 0);
        var blocks = new BlockRegistry([frame, portal,
            new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        for (var cx = 0; cx <= maxX / Chunk.Size; cx++)
        for (var cy = 0; cy <= maxY / Chunk.Size; cy++)
            world.InsertChunk(new ChunkCoord(cx, cy, 0), new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        return (new PortalActivationRuntime(world, blocks, mutations),
            world, mutations, blocks);
    }

    private static InventoryStack Slicer(string dimension = "asteria:umbral") =>
        new(InventoryEntry.FromItem("asteria:slicer_dimensional",
            new Dictionary<string, string> { ["target_dimension"] = dimension }));

    private static WorldVoxelCoord Frame(
        VoxelMutationRuntime mutations, BlockRegistry blocks,
        int minX = 2, int maxX = 5, int minY = 3, int maxY = 6,
        int z = 5, bool open = false)
    {
        var glass = blocks.GetId("asteria:glass");
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var edge = x == minX || x == maxX || y == minY || y == maxY;
            if (edge && (!open || x != minX || y != minY + 1))
                Assert.True(mutations.SetBlockAt(
                    new WorldVoxelCoord(x, y, z), glass, out _));
        }
        return new WorldVoxelCoord(minX, minY, z);
    }

    [Fact]
    public void ClosedGlassFrameCreatesPersistentCenteredPortalInEmptyCells()
    {
        var (portal, world, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        var before = world.Revision;
        Assert.True(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out var target));
        Assert.Equal("asteria:umbral", target);
        Assert.True(world.Revision > before);
        for (var x = 3; x <= 4; x++)
        for (var y = 4; y <= 5; y++)
        {
            var at = new WorldVoxelCoord(x, y, 5);
            Assert.True(portal.TryGetDestinationAt(at, out var destination));
            Assert.Equal("asteria:umbral", destination);
            Assert.Equal("asteria:portal", blocks.GetDefinition(
                world.GetCellOrEmpty(at).Block).Id);
            Assert.Equal("asteria:umbral",
                world.GetBlockSurfaceStateOrEmpty(at).MetaTags["target_dimension"]);
            Assert.Equal(BlockOrientation.Z, world.GetCellOrEmpty(at).Orientation);
        }
        Assert.False(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out _));
    }

    [Fact]
    public void SamePortalBlockSupportsDifferentInstanceDestinations()
    {
        var (portal, world, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        Assert.True(portal.TryActivate(Slicer("asteria:overworld"),
            new VoxelWorldHit(frame, 0, 0, 1), out var target));
        Assert.Equal("asteria:overworld", target);
        var at = new WorldVoxelCoord(3, 4, 5);
        Assert.Equal(blocks.GetId("asteria:portal"),
            world.GetCellOrEmpty(at).Block);
        Assert.True(portal.TryGetDestinationAt(at, out target));
        Assert.Equal("asteria:overworld", target);
        Assert.Equal("asteria:overworld",
            world.GetBlockSurfaceStateOrEmpty(at).MetaTags["target_dimension"]);

        Assert.True(mutations.SetCellAt(
            new WorldVoxelCoord(2, 4, 5), VoxelCell.Empty, out _));
        Assert.False(portal.TryGetDestinationAt(at, out _));
    }

    [Fact]
    public void PortalInstanceMetadataPersistsThroughChunkRoundTrip()
    {
        var (portal, world, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        Assert.True(portal.TryActivate(Slicer("asteria:overworld"),
            new VoxelWorldHit(frame, 0, 0, 1), out _));

        var dyes = new DyeRegistry([]);
        var layers = new AttachedLayerRegistry([]);
        var fluids = new FluidRegistry([]);
        var bytes = ChunkSaveCodec.Encode(world.GetChunk(ChunkCoord.Zero),
            blocks, fluids, dyes, layers);
        var loaded = ChunkSaveCodec.Decode(bytes, blocks, fluids, dyes, layers);
        var restored = new VoxelWorld();
        restored.InsertChunk(ChunkCoord.Zero, loaded);
        var restoredMutations = new VoxelMutationRuntime(
            restored, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var restoredPortal = new PortalActivationRuntime(
            restored, blocks, restoredMutations);

        Assert.True(restoredPortal.TryGetDestinationAt(
            new WorldVoxelCoord(3, 4, 5), out var destination));
        Assert.Equal("asteria:overworld", destination);
        var returningPlayer = new WorldAabb(
            new Vector3(2.45f, 4.05f, 4.85f),
            new Vector3(3.15f, 5.75f, 5.55f));
        Assert.True(restoredPortal.TryGetDestinationIntersecting(
            returningPlayer, out var roundTripTarget));
        Assert.Equal("asteria:overworld", roundTripTarget);

        Assert.True(restoredMutations.SetCellAt(
            new WorldVoxelCoord(2, 4, 5), VoxelCell.Empty, out _));
        Assert.False(restoredPortal.TryGetDestinationAt(
            new WorldVoxelCoord(3, 4, 5), out _));
        Assert.False(restoredPortal.TryGetDestinationIntersecting(
            returningPlayer, out _));
    }

    [Fact]
    public void BreakingGlassBoundaryRemovesItsWholePortalInterior()
    {
        var (portal, world, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        Assert.True(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out _));
        Assert.True(mutations.SetCellAt(
            new WorldVoxelCoord(2, 4, 5), VoxelCell.Empty, out _));
        for (var x = 3; x <= 4; x++)
        for (var y = 4; y <= 5; y++)
        {
            var at = new WorldVoxelCoord(x, y, 5);
            Assert.False(portal.TryGetDestinationAt(at, out _));
            Assert.True(world.GetCellOrEmpty(at).IsEmpty);
        }
    }

    [Fact]
    public void OpenFrameAndOccupiedInteriorRejectAtomically()
    {
        var (portal, world, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks, open: true);
        var before = world.Revision;
        Assert.False(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out _));
        Assert.Equal(before, world.Revision);

        mutations.SetBlockAt(new WorldVoxelCoord(2, 4, 5),
            blocks.GetId("asteria:glass"), out _);
        mutations.SetBlockAt(new WorldVoxelCoord(3, 4, 5),
            blocks.GetId("asteria:stone"), out _);
        before = world.Revision;
        Assert.False(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out _));
        Assert.Equal(before, world.Revision);
    }


    [Fact]
    public void LargePortalSpansChunksAndGlassBreakRetractsEveryInteriorCell()
    {
        // A 60x60 frame encloses 3,364 cells: previously rejected by the
        // 512-voxel Asteria cap despite being valid in MineClone.
        const int edge = 61;
        var (portal, world, mutations, blocks) = Fixture(edge, edge);
        var frame = Frame(mutations, blocks,
            minX: 2, maxX: edge, minY: 2, maxY: edge);
        Assert.True(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out var destination));
        Assert.Equal("asteria:umbral", destination);

        foreach (var at in new[]
        {
            new WorldVoxelCoord(3, 3, 5),
            new WorldVoxelCoord(Chunk.Size, Chunk.Size, 5),
            new WorldVoxelCoord(edge - 1, edge - 1, 5),
        })
        {
            Assert.True(portal.TryGetDestinationAt(at, out var target));
            Assert.Equal(destination, target);
        }

        // A boundary edit retracts even far-away cells in other chunks.
        Assert.True(mutations.SetCellAt(
            new WorldVoxelCoord(2, 30, 5), VoxelCell.Empty, out _));
        foreach (var at in new[]
        {
            new WorldVoxelCoord(3, 3, 5),
            new WorldVoxelCoord(Chunk.Size, Chunk.Size, 5),
            new WorldVoxelCoord(edge - 1, edge - 1, 5),
        })
        {
            Assert.False(portal.TryGetDestinationAt(at, out _));
            Assert.True(world.GetCellOrEmpty(at).IsEmpty);
        }
    }

    [Fact]
    public void MoreThan4096PortalCellsRejectWithoutPartialWorldMutation()
    {
        // Center the selected glass segment on the upper frame edge so
        // no valid candidate is discarded by the separate 64-block span.
        const int maxX = 81;
        const int maxY = 61;
        var (portal, world, mutations, blocks) = Fixture(maxX, maxY);
        Frame(mutations, blocks, minX: 2, maxX: maxX,
            minY: 2, maxY: maxY);
        var before = world.Revision;
        Assert.False(portal.TryActivate(Slicer(),
            new VoxelWorldHit(new WorldVoxelCoord(40, 2, 5), 0, 0, 1),
            out _));
        Assert.Equal(before, world.Revision);
        Assert.True(world.GetCellOrEmpty(
            new WorldVoxelCoord(40, 20, 5)).IsEmpty);
    }

    [Fact]
    public void NoUnknownTargetOrWrongItemOrUnloadedInterior()
    {
        var (portal, world, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        Assert.False(portal.TryActivate(Slicer("asteria:nowhere"),
            new VoxelWorldHit(frame, 0, 0, 1), out _));
        Assert.False(portal.TryActivate(new InventoryStack(
                InventoryEntry.FromItem("asteria:pebble")),
            new VoxelWorldHit(frame, 0, 0, 1), out _));
        Assert.False(portal.TryActivate(null,
            new VoxelWorldHit(frame, 0, 0, 1), out _));
        Assert.True(world.GetCellOrEmpty(new WorldVoxelCoord(3, 4, 5)).IsEmpty);
    }

    [Fact]
    public void PhysicalPortalRequiresExactSafeResidentArrival()
    {
        var (_, world, mutations, blocks) = Fixture();
        var feet = new WorldVoxelCoord(4, 4, 4);
        Assert.False(ResidentWarpDestinationQuery.IsExactSafe(
            world, new System.Numerics.Vector3(4.5f, 4, 4.5f)));
        mutations.SetBlockAt(feet + (0, -1, 0),
            blocks.GetId("asteria:stone"), out _);
        Assert.True(ResidentWarpDestinationQuery.IsExactSafe(
            world, new System.Numerics.Vector3(4.5f, 4, 4.5f)));
        mutations.SetBlockAt(feet, blocks.GetId("asteria:stone"), out _);
        Assert.False(ResidentWarpDestinationQuery.IsExactSafe(
            world, new System.Numerics.Vector3(4.5f, 4, 4.5f)));
    }

    [Fact]
    public void DefaultPackHasTaggedGlassAndTwoDestinations()
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "blocks");
        var glass = BlockDefinitionJson.Parse(
            File.ReadAllText(Path.Combine(root, "glass.json")));
        Assert.True(glass.HasTag("is_portal_frame"));
        Assert.True(glass.HasTag("accept_dimension:asteria:umbral"));
        var definition = BlockDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(root, "portal.json")));
        Assert.Equal("asteria:portal", definition.Id);
        Assert.True(definition.Shape.IsCenteredLayer);
        Assert.True(definition.HasTag("creative_hidden"));
    }
    private static void PlaceOrientedPortal(
        VoxelMutationRuntime mutations, BlockRegistry blocks,
        WorldVoxelCoord at, BlockOrientation orientation, string dimension)
    {
        var snapshot = new BlockStateSnapshot(
            new VoxelCell(blocks.GetId("asteria:portal"),
                orientation: orientation),
            MicroblockMask.Empty,
            new BlockSurfaceState(null,
                metaTags: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PortalActivationRuntime.TargetMetadataKey] = dimension,
                }));
        Assert.True(mutations.ApplyStructureChanges(
            [new VoxelStructureChange(at, snapshot, FluidCell.Empty)]));
    }

    [Fact]
    public void PortalContactsColliderEvenWhenFeetVoxelIsNotPortal()
    {
        var (portal, _, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        Assert.True(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out _));

        // Player's center is in the glass column X=2, while the side of
        // their 0.7-wide capsule reaches the portal cell at X=3.
        var body = new WorldAabb(
            new Vector3(2.45f, 4.05f, 4.85f),
            new Vector3(3.15f, 5.75f, 5.55f));
        Assert.True(portal.TryGetDestinationIntersecting(body, out var target));
        Assert.Equal("asteria:umbral", target);
    }

    [Fact]
    public void PortalDoesNotActivateWithoutTouchingItsThinPlane()
    {
        var (portal, _, mutations, blocks) = Fixture();
        var frame = Frame(mutations, blocks);
        Assert.True(portal.TryActivate(Slicer(),
            new VoxelWorldHit(frame, 0, 0, 1), out _));

        // Occupying the same grid voxel as a portal does not prove
        // physical contact with the centered 0.1-thick Z plane.
        var body = new WorldAabb(
            new Vector3(3.2f, 4.0f, 5.0f),
            new Vector3(3.8f, 5.7f, 5.40f));
        Assert.False(portal.TryGetDestinationIntersecting(body, out _));
    }

    [Theory]
    [InlineData(BlockOrientation.X)]
    [InlineData(BlockOrientation.Y)]
    [InlineData(BlockOrientation.Z)]
    public void PortalContactUsesAuthoredOrientation(BlockOrientation orientation)
    {
        var (portal, _, mutations, blocks) = Fixture();
        var at = new WorldVoxelCoord(7, 7, 7);
        PlaceOrientedPortal(mutations, blocks, at, orientation, "asteria:umbral");

        var near = orientation switch
        {
            BlockOrientation.X => new WorldAabb(
                new Vector3(7.0f, 7.1f, 7.1f),
                new Vector3(7.40f, 7.9f, 7.9f)),
            BlockOrientation.Y => new WorldAabb(
                new Vector3(7.1f, 7.0f, 7.1f),
                new Vector3(7.9f, 7.40f, 7.9f)),
            _ => new WorldAabb(
                new Vector3(7.1f, 7.1f, 7.0f),
                new Vector3(7.9f, 7.9f, 7.40f)),
        };
        Assert.False(portal.TryGetDestinationIntersecting(near, out _));

        var through = new WorldAabb(
            new Vector3(7.1f, 7.1f, 7.1f),
            new Vector3(7.9f, 7.9f, 7.9f));
        Assert.True(portal.TryGetDestinationIntersecting(through, out var target));
        Assert.Equal("asteria:umbral", target);
    }

    [Fact]
    public void OverlappingDifferentPortalTargetsNeverChooseAnArbitrarySphere()
    {
        var (portal, _, mutations, blocks) = Fixture();
        PlaceOrientedPortal(mutations, blocks,
            new WorldVoxelCoord(3, 4, 5), BlockOrientation.Z,
            "asteria:umbral");
        PlaceOrientedPortal(mutations, blocks,
            new WorldVoxelCoord(4, 4, 5), BlockOrientation.Z,
            "asteria:overworld");

        var overlap = new WorldAabb(
            new Vector3(3.6f, 4.0f, 5.46f),
            new Vector3(4.3f, 5.7f, 5.56f));
        Assert.False(portal.TryGetDestinationIntersecting(overlap, out var target));
        Assert.Empty(target);

        var firstOnly = new WorldAabb(
            new Vector3(3.05f, 4.0f, 5.46f),
            new Vector3(3.75f, 5.7f, 5.56f));
        Assert.True(portal.TryGetDestinationIntersecting(firstOnly, out target));
        Assert.Equal("asteria:umbral", target);
    }

    [Fact]
    public void ContactQueryIsBoundedAndRejectsDegenerateBounds()
    {
        var (portal, _, _, _) = Fixture();
        Assert.False(portal.TryGetDestinationIntersecting(new WorldAabb(
            new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 1f)), out _));
        Assert.False(portal.TryGetDestinationIntersecting(new WorldAabb(
            new Vector3(0f, 0f, 0f), new Vector3(10000f, 1f, 1f)), out _));
        Assert.False(portal.TryGetDestinationIntersecting(new WorldAabb(
            new Vector3(0f, -1f, 0f), new Vector3(1f, 1f, 1f)), out _));
    }


}
