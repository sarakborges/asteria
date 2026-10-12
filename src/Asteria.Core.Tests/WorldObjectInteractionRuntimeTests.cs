using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldObjectInteractionRuntimeTests
{
    private const string Flower = """
      {"id":"asteria:flower","category":"foliage",
       "icon":"textures/objects/daisy.png","dropSelf":true,
       "visual":{"type":"crossedSprite","texture":"textures/objects/daisy.png",
                 "width":0.7,"height":0.65}}
      """;
    private const string Grass = """
      {"id":"asteria:grass","category":"foliage",
       "icon":"textures/objects/grass.png","dropSelf":false,
       "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png",
                 "width":0.7,"height":0.65}}
      """;
    private const string Pickup = """
      {"id":"asteria:pebble_object","category":"foliage",
       "icon":"textures/objects/seashell.png","interaction":"pickup",
       "visual":{"type":"crossedSprite","texture":"textures/objects/seashell.png",
                 "width":0.7,"height":0.65}}
      """;

    private static (WorldObjectInteractionRuntime Runtime, VoxelWorld World,
        VoxelMutationRuntime Mutations, DroppedBlockRuntime Drops,
        PackContentRegistry<ObjectDefinition> Definitions) Setup()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            [Flower, Grass, Pickup], ObjectDefinition.Parse, d => d.Id);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        return (new WorldObjectInteractionRuntime(
            world, blocks, objects, mutations, drops), world, mutations,
            drops, objects);
    }

    [Fact]
    public void PlacesOnAuthoredSupportFaceWithoutChangingBlockOrSourceWater()
    {
        var (runtime, world, mutations, _, _) = Setup();
        var support = new WorldVoxelCoord(2, 2, 2);
        var adjacent = support + (0, 1, 0);
        Assert.True(mutations.SetBlockAt(support, new BlockRuntimeId(1), out _));
        var water = FluidCell.Source(new FluidRuntimeId(1));
        Assert.True(mutations.SetFluidAt(adjacent, water, out _));
        var entry = new InventoryStack(InventoryEntry.FromObject("asteria:flower"), 3);
        var hit = new VoxelWorldHit(support, 0, 1, 0);
        Assert.True(runtime.TryPlace(entry, hit));
        Assert.False(runtime.TryPlace(entry, hit)); // no identical duplicate
        Assert.True(world.TryGetObjects(support, out var objects));
        var obj = Assert.Single(objects);
        Assert.Equal("asteria:flower", obj.Id);
        Assert.Equal(BlockFace.Top, obj.Face);
        Assert.Equal(water, world.GetFluidOrEmpty(adjacent));
        Assert.Equal(new BlockRuntimeId(1), world.GetCellOrEmpty(support).Block);
        Assert.True(world.GetCellOrEmpty(adjacent).IsEmpty);
        Assert.False(runtime.TryPlace(entry,
            new VoxelWorldHit(support, 1, 0, 0))); // unsupported face
        Assert.False(runtime.TryPlace(entry,
            new VoxelWorldHit(support, 0, 0, 0))); // invalid normal
    }

    [Fact]
    public void BlockedDestinationOrAirSupportRejectPlacement()
    {
        var (runtime, world, mutations, _, _) = Setup();
        var hit = new VoxelWorldHit(new WorldVoxelCoord(2, 2, 2), 0, 1, 0);
        var entry = new InventoryStack(InventoryEntry.FromObject("asteria:grass"));
        Assert.False(runtime.TryPlace(entry, hit));
        mutations.SetBlockAt(hit.Voxel, new BlockRuntimeId(1), out _);
        mutations.SetBlockAt(hit.Voxel + (0, 1, 0), new BlockRuntimeId(1), out _);
        Assert.False(runtime.TryPlace(entry, hit));
        Assert.False(runtime.TryPlace(
            new InventoryStack(InventoryEntry.FromItem("asteria:grass")), hit));
        Assert.False(runtime.TryPlace(
            new InventoryStack(InventoryEntry.FromObject("asteria:unknown")), hit));
        Assert.False(runtime.TryPlace(entry, new VoxelWorldHit(
            new WorldVoxelCoord(40, 2, 2), 0, 1, 0)));
    }

    [Fact]
    public void BreakingSurvivalObjectDropsExactObjectInventoryKind()
    {
        var (runtime, world, mutations, drops, _) = Setup();
        var support = new WorldVoxelCoord(2, 2, 2);
        mutations.SetBlockAt(support, new BlockRuntimeId(1), out _);
        var obj = new ObjectCell("asteria:flower", BlockFace.Top);
        mutations.AddObjectAt(support, obj);
        Assert.True(runtime.TryBreak(new WorldObjectHit(support, obj, 1f),
            PlayerGameMode.Survival));
        Assert.True(world.TryGetObjects(support, out var objects));
        Assert.Empty(objects);
        var drop = Assert.Single(drops.ActiveBlocks);
        Assert.Equal(InventoryEntryKind.Object, drop.Stack.Kind);
        Assert.Equal(obj.Id, drop.Stack.Id);
        Assert.True(drop.Position.Y > support.Y + 1);
        Assert.False(runtime.TryBreak(new WorldObjectHit(support, obj, 1f),
            PlayerGameMode.Survival));
        Assert.Single(drops.ActiveBlocks);
    }

    [Fact]
    public void CreativeBreakAndDropSelfFalseDoNotSpawnDrops()
    {
        var (runtime, world, mutations, drops, _) = Setup();
        var support = new WorldVoxelCoord(2, 2, 2);
        mutations.SetBlockAt(support, new BlockRuntimeId(1), out _);
        var flower = new ObjectCell("asteria:flower", BlockFace.Top);
        var grass = new ObjectCell("asteria:grass", BlockFace.Top);
        mutations.AddObjectAt(support, flower);
        mutations.AddObjectAt(support, grass);
        Assert.True(runtime.TryBreak(new WorldObjectHit(support, flower, 1f),
            PlayerGameMode.Creative));
        Assert.True(runtime.TryBreak(new WorldObjectHit(support, grass, 1f),
            PlayerGameMode.Survival));
        Assert.Empty(drops.ActiveBlocks);
        Assert.False(world.TryGetObjects(support, out var remaining) &&
            remaining.Count > 0);
    }

    [Fact]
    public void PickupMovesObjectToInventoryOnlyWhenItCanFit()
    {
        var (runtime, world, mutations, _, _) = Setup();
        var support = new WorldVoxelCoord(2, 2, 2);
        mutations.SetBlockAt(support, new BlockRuntimeId(1), out _);
        var pickup = new ObjectCell("asteria:pebble_object", BlockFace.Top);
        mutations.AddObjectAt(support, pickup);
        var hit = new WorldObjectHit(support, pickup, 1f);
        var player = new PlayerInventory();
        Assert.False(runtime.TryBreak(hit, PlayerGameMode.Survival));
        Assert.True(runtime.TryPickup(hit, player));
        var snapshot = player.Capture();
        Assert.Contains(snapshot.Backpack.Concat(snapshot.Hotbar),
            stack => stack is { Kind: InventoryEntryKind.Object,
                Id: "asteria:pebble_object", Quantity: 1 });
        Assert.False(runtime.TryPickup(hit, player));
        Assert.True(world.TryGetObjects(support, out var objects));
        Assert.Empty(objects);
    }

    [Fact]
    public void FullInventoryRejectsPickupWithoutRemovingObject()
    {
        var (runtime, world, mutations, _, _) = Setup();
        var support = new WorldVoxelCoord(2, 2, 2);
        mutations.SetBlockAt(support, new BlockRuntimeId(1), out _);
        var obj = new ObjectCell("asteria:pebble_object", BlockFace.Top);
        Assert.True(mutations.AddObjectAt(support, obj));
        var inventory = new PlayerInventory();
        var filler = new InventoryStack(
            InventoryEntry.FromItem("asteria:filler"), 64);
        for (var index = 0; index < PlayerInventory.TotalSlots; index++)
            Assert.True(inventory.TryInsert(filler));
        var before = inventory.Revision;
        Assert.False(runtime.TryPickup(
            new WorldObjectHit(support, obj, 1f), inventory));
        Assert.Equal(before, inventory.Revision);
        Assert.True(world.TryGetObjects(support, out var existing));
        Assert.Contains(obj, existing);
    }

    [Fact]
    public void PortableObjectStacksRoundTripAndRetainKind()
    {
        var blocks = new BlockRegistry([]);
        var dyes = new DyeRegistry([]);
        var layers = new AttachedLayerRegistry([]);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            PortableStackSaveCodec.Write(writer,
                new InventoryStack(InventoryEntry.FromObject("asteria:flower"), 17),
                blocks, dyes, layers);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var restored = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
        Assert.Equal(InventoryEntryKind.Object, restored!.Kind);
        Assert.Equal(17, restored.Quantity);
        Assert.Equal("asteria:flower", restored.Id);
    }
}
