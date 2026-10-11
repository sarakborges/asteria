using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PortalActivationRuntimeTests
{
    private static (PortalActivationRuntime Portal, VoxelWorld World,
        VoxelMutationRuntime Mutations, BlockRegistry Blocks) Fixture()
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
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        return (new PortalActivationRuntime(world, blocks, mutations),
            world, mutations, blocks);
    }

    private static InventoryStack Slicer(string dimension = "asteria:umbral") =>
        new(InventoryEntry.FromItem("asteria:dimensional_slicer",
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
}
