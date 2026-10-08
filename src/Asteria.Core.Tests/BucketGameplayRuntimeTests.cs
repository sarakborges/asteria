using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BucketGameplayRuntimeTests
{
    private static readonly WorldVoxelCoord Source = new(2, 4, 5);
    private static readonly WorldVoxelCoord TargetBlock = new(3, 4, 4);
    private static readonly WorldVoxelCoord PourAt = new(3, 4, 5);

    private sealed record Fixture(
        VoxelWorld World,
        VoxelMutationRuntime Mutations,
        FluidRegistry Fluids,
        PlayerInventory Inventory,
        BucketGameplayRuntime Bucket,
        BlockRegistry Blocks);

    private static Fixture Setup()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(25, 120, 235), 0.8f),
        ]);
        var tools = PackContentRegistry<ToolDefinition>.FromJson([
            """
            {
              "id":"asteria:bucket",
              "category":"tools",
              "icon":"textures/tools/iron_bucket_empty.png",
              "leftBehavior":"asteria:none",
              "rightBehavior":"asteria:bucket/use"
            }
            """
        ], ToolDefinition.Parse, d => d.Id);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromTool("asteria:bucket"))));
        return new Fixture(
            world, mutations, fluids, inventory,
            new BucketGameplayRuntime(world, fluids, mutations, tools),
            blocks);
    }

    [Fact]
    public void CollectsOnlySourceAndPoursIntoAdjacentLoadedEmptyVoxel()
    {
        var fixture = Setup();
        var water = fixture.Fluids.GetId("asteria:water");
        Assert.True(fixture.Mutations.SetFluidAt(
            Source, FluidCell.Source(water), out _));
        var origin = new Vector3(2.5f, 4.5f, 3.5f);
        var forward = Vector3.UnitZ;

        Assert.True(fixture.Bucket.IsEquipped(fixture.Inventory.SelectedStack));
        Assert.True(fixture.Bucket.TryUse(fixture.Inventory, origin, forward, null));
        Assert.True(fixture.World.GetFluidOrEmpty(Source).IsEmpty);
        Assert.Equal("asteria:water",
            fixture.Inventory.SelectedStack!.Entry.Metadata[
                BucketGameplayRuntime.FluidMetadataKey]);

        Assert.True(fixture.Mutations.SetBlockAt(
            TargetBlock, fixture.Blocks.GetId("asteria:stone"), out _));
        Assert.True(fixture.Bucket.TryUse(
            fixture.Inventory, origin, forward,
            new VoxelWorldHit(TargetBlock, 0, 0, 1)));
        Assert.True(fixture.World.GetFluidOrEmpty(PourAt).IsSource);
        Assert.Equal(water, fixture.World.GetFluidOrEmpty(PourAt).Fluid);
        Assert.Empty(fixture.Inventory.SelectedStack!.Entry.Metadata);
        Assert.Equal(1, fixture.Inventory.SelectedStack.Quantity);
    }

    [Fact]
    public void FlowingFluidIsNotCollectedAndSolidBlocksOccludeSources()
    {
        var fixture = Setup();
        var water = fixture.Fluids.GetId("asteria:water");
        var origin = new Vector3(2.5f, 4.5f, 3.5f);
        Assert.True(fixture.Mutations.SetFluidAt(Source,
            FluidCell.Spreading(water, 4, 2), out _));
        Assert.Null(fixture.Bucket.FindCollectibleSource(
            origin, Vector3.UnitZ, 8));
        Assert.False(fixture.Bucket.TryUse(
            fixture.Inventory, origin, Vector3.UnitZ, null));
        Assert.True(fixture.Mutations.SetFluidAt(
            Source, FluidCell.Source(water), out _));
        Assert.True(fixture.Mutations.SetBlockAt(
            new WorldVoxelCoord(2, 4, 4),
            fixture.Blocks.GetId("asteria:stone"), out _));
        Assert.Null(fixture.Bucket.FindCollectibleSource(
            origin, Vector3.UnitZ, 8));
        Assert.False(fixture.Bucket.TryUse(
            fixture.Inventory, origin, Vector3.UnitZ, null));
        Assert.True(fixture.World.GetFluidOrEmpty(Source).IsSource);
        Assert.Empty(fixture.Inventory.SelectedStack!.Entry.Metadata);
    }

    [Fact]
    public void PlacementCannotOverwriteSolidOrOtherFluid()
    {
        var fixture = Setup();
        var water = fixture.Fluids.GetId("asteria:water");
        var empty = fixture.Inventory.SelectedStack!;
        Assert.True(fixture.Inventory.TryReplaceSelected(
            empty, InventoryEntry.FromTool(
                "asteria:bucket",
                new Dictionary<string, string>
                {
                    [BucketGameplayRuntime.FluidMetadataKey] = "asteria:water",
                })));
        Assert.True(fixture.Mutations.SetBlockAt(
            TargetBlock, fixture.Blocks.GetId("asteria:stone"), out _));
        Assert.True(fixture.Mutations.SetBlockAt(
            PourAt, fixture.Blocks.GetId("asteria:stone"), out _));
        var hit = new VoxelWorldHit(TargetBlock, 0, 0, 1);
        Assert.False(fixture.Bucket.TryUse(
            fixture.Inventory, Vector3.Zero, Vector3.UnitZ, hit));
        Assert.Equal("asteria:water", fixture.Inventory.SelectedStack!.Entry.Metadata[
            BucketGameplayRuntime.FluidMetadataKey]);

        Assert.True(fixture.Mutations.SetCellAt(PourAt, VoxelCell.Empty, out _));
        Assert.True(fixture.Mutations.SetFluidAt(
            PourAt, FluidCell.Spreading(water, 4, 1), out _));
        Assert.False(fixture.Bucket.TryUse(
            fixture.Inventory, Vector3.Zero, Vector3.UnitZ, hit));
        Assert.Equal(4, fixture.World.GetFluidOrEmpty(PourAt).Level);
    }

    [Fact]
    public void FluidTransferPreservesUnrelatedBucketMetadata()
    {
        var fixture = Setup();
        var original = fixture.Inventory.SelectedStack!;
        Assert.True(fixture.Inventory.TryReplaceSelected(
            original, InventoryEntry.FromTool(
                "asteria:bucket",
                new Dictionary<string, string> { ["owner"] = "player" })));
        Assert.True(fixture.Mutations.SetFluidAt(
            Source, FluidCell.Source(fixture.Fluids.GetId("asteria:water")),
            out _));

        Assert.True(fixture.Bucket.TryUse(
            fixture.Inventory,
            new Vector3(2.5f, 4.5f, 3.5f), Vector3.UnitZ, null));
        var filled = fixture.Inventory.SelectedStack!;
        Assert.Equal("player", filled.Entry.Metadata["owner"]);
        Assert.Equal("asteria:water",
            filled.Entry.Metadata[BucketGameplayRuntime.FluidMetadataKey]);

        Assert.True(fixture.Mutations.SetBlockAt(
            TargetBlock, fixture.Blocks.GetId("asteria:stone"), out _));
        Assert.True(fixture.Bucket.TryUse(
            fixture.Inventory,
            Vector3.Zero, Vector3.UnitZ,
            new VoxelWorldHit(TargetBlock, 0, 0, 1)));
        var emptied = fixture.Inventory.SelectedStack!;
        Assert.Equal("player", emptied.Entry.Metadata["owner"]);
        Assert.False(emptied.Entry.Metadata.ContainsKey(
            BucketGameplayRuntime.FluidMetadataKey));
    }

    [Fact]
    public void RejectsStaleBlockTargetBeforePlacingFluid()
    {
        var fixture = Setup();
        var old = fixture.Inventory.SelectedStack!;
        Assert.True(fixture.Inventory.TryReplaceSelected(
            old, InventoryEntry.FromTool(
                "asteria:bucket",
                new Dictionary<string, string>
                {
                    [BucketGameplayRuntime.FluidMetadataKey] = "asteria:water",
                })));
        Assert.False(fixture.Bucket.TryUse(
            fixture.Inventory, Vector3.Zero, Vector3.UnitZ,
            new VoxelWorldHit(TargetBlock, 0, 0, 1)));
        Assert.True(fixture.World.GetFluidOrEmpty(PourAt).IsEmpty);
        Assert.Contains(BucketGameplayRuntime.FluidMetadataKey,
            fixture.Inventory.SelectedStack!.Entry.Metadata.Keys);
    }

    [Fact]
    public void MetadataTransitionCannotOverwriteChangedOrMultipleSelectedItems()
    {
        var fixture = Setup();
        var old = fixture.Inventory.SelectedStack!;
        Assert.False(fixture.Inventory.TryReplaceSelected(
            new InventoryStack(InventoryEntry.FromTool("asteria:not_bucket")),
            InventoryEntry.FromTool("asteria:bucket")));
        Assert.False(fixture.Inventory.TryReplaceSelected(
            old, InventoryEntry.FromTool("asteria:other_tool")));
        Assert.True(fixture.Inventory.TryReplaceSelected(
            old, InventoryEntry.FromTool("asteria:bucket",
                new Dictionary<string, string> { ["contained_fluid"] = "asteria:water" })));
        Assert.False(fixture.Inventory.TryReplaceSelected(
            old, InventoryEntry.FromTool("asteria:bucket")));
    }
}
