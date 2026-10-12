using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PebbleLilyPadObjectTests
{
    private const string Pebble = """
        {"id":"asteria:pebble","category":"crafting_materials",
         "icon":"textures/items/pebble.png",
         "interaction":"pickup","pickupItem":"asteria:pebble",
         "visual":{"type":"extrudedSprite","texture":"textures/items/pebble.png",
                   "size":[0.42,0.42],"height":0.012},
         "placementFaces":["top"]}
        """;
    private const string Lily = """
        {"id":"asteria:lily_pad","category":"foliage",
         "icon":"textures/objects/lily_pad.png",
         "supportFluid":"asteria:water","dropSelf":false,
         "visual":{"type":"extrudedSprite","texture":"textures/objects/lily_pad.png",
                   "size":[0.89,0.89],"height":0.012},
         "placementFaces":["top"]}
        """;

    private static PackContentRegistry<ObjectDefinition> Definitions() =>
        PackContentRegistry<ObjectDefinition>.FromJson(
            [Pebble, Lily], ObjectDefinition.Parse, d => d.Id);

    private static (VoxelWorld World, VoxelMutationRuntime Mutations,
        BlockRegistry Blocks, FluidRegistry Fluids,
        WorldObjectInteractionRuntime Interactions) Setup()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 100, 255), .7f)
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var runtime = new WorldObjectInteractionRuntime(
            world, blocks, Definitions(), mutations, drops, fluids);
        return (world, mutations, blocks, fluids, runtime);
    }

    [Fact]
    public void PickingUpPebbleYieldsThePreexistingItemNotAnObject()
    {
        var (world, mutations, blocks, _, runtime) = Setup();
        var support = new WorldVoxelCoord(4, 4, 4);
        Assert.True(mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _));
        var cell = new ObjectCell("asteria:pebble", BlockFace.Top);
        Assert.True(mutations.AddObjectAt(support, cell));

        var inventory = new PlayerInventory();
        Assert.True(runtime.TryPickup(new WorldObjectHit(support, cell, 1f), inventory));
        var snapshot = inventory.Capture();
        Assert.Contains(snapshot.Backpack.Concat(snapshot.Hotbar), stack =>
            stack is { Kind: InventoryEntryKind.Item, Id: "asteria:pebble", Quantity: 1 });
        Assert.Empty(world.TryGetObjects(support, out var remaining) ? remaining : []);
    }

    [Fact]
    public void LilyPadPlacesOnWaterSourceAndDetachesWhenWaterDisappears()
    {
        var (world, mutations, blocks, fluids, runtime) = Setup();
        var support = new WorldVoxelCoord(4, 5, 4);
        var source = FluidCell.Source(fluids.GetId("asteria:water"));
        Assert.True(mutations.SetFluidAt(support, source, out _));

        var selected = new InventoryStack(InventoryEntry.FromObject("asteria:lily_pad"));
        var hit = new VoxelWorldHit(support, 0, 1, 0);
        var decision = ObjectPlacementResolver.Resolve(
            world, blocks, Definitions(), selected, hit, fluids);
        Assert.True(decision.Accepted);
        Assert.True(runtime.TryPlace(selected, hit));
        Assert.Equal(source, world.GetFluidOrEmpty(support));
        Assert.True(world.GetCellOrEmpty(support).IsEmpty);
        Assert.Equal("asteria:lily_pad", Assert.Single(
            world.TryGetObjects(support, out var attached) ? attached : []).Id);
        Assert.False(runtime.TryPlace(selected, hit));

        var changed = new List<ChunkCoord>();
        world.ObjectChunkChanged += changed.Add;
        Assert.True(mutations.SetFluidAt(support, FluidCell.Empty, out _));
        Assert.Empty(world.TryGetObjects(support, out var after) ? after : []);
        Assert.Contains(ChunkCoord.Zero, changed);
        Assert.Empty(world.ResidentObjectChunkCoords);
        Assert.False(runtime.TryPlace(selected, hit));
    }

    [Fact]
    public void LilyPadRequiresFullWaterAndFluidRayHonorsOcclusion()
    {
        var (world, mutations, blocks, fluids, runtime) = Setup();
        var support = new WorldVoxelCoord(5, 5, 5);
        var water = fluids.GetId("asteria:water");
        var selected = new InventoryStack(InventoryEntry.FromObject("asteria:lily_pad"));
        var hit = new VoxelWorldHit(support, 0, 1, 0);
        Assert.True(mutations.SetFluidAt(support, FluidCell.Spreading(water, 7, 1), out _));
        Assert.False(runtime.TryPlace(selected, hit));
        Assert.True(mutations.SetFluidAt(support, FluidCell.Source(water), out _));
        Assert.Null(VoxelWorldRaycaster.Raycast(world, blocks,
            new Vector3(5.5f, 7.5f, 5.5f), -Vector3.UnitY, 5));
        Assert.Equal(hit, VoxelWorldRaycaster.Raycast(world, blocks,
            new Vector3(5.5f, 7.5f, 5.5f), -Vector3.UnitY, 5,
            includeFluidSurface: true));
        Assert.True(mutations.SetBlockAt(support + (0, 1, 0),
            blocks.GetId("asteria:stone"), out _));
        Assert.NotEqual(hit, VoxelWorldRaycaster.Raycast(world, blocks,
            new Vector3(5.5f, 7.5f, 5.5f), -Vector3.UnitY, 5,
            includeFluidSurface: true));
    }

    [Fact]
    public void PackUsesExtrudedObjectsAndPreservesWaterSurfaceDecoratorContract()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");
        IEnumerable<string> Docs(string dir) =>
            Directory.EnumerateFiles(Path.Combine(root, dir), "*.json")
                .OrderBy(x => x, StringComparer.Ordinal).Select(File.ReadAllText);
        var blocks = BlockRegistry.FromJson(Docs("blocks"));
        var biomes = BiomeRegistry.FromJson(Docs("biomes"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Docs("objects"), ObjectDefinition.Parse, d => d.Id);
        biomes.ValidateObjects(objects);
        foreach (var id in new[] { "pebble", "lily_pad" })
        {
            Assert.False(blocks.TryGetId("asteria:" + id, out _));
            Assert.Equal(ObjectVisualKind.ExtrudedSprite,
                objects.Get("asteria:" + id).Visual.Kind);
        }
        Assert.Equal(InventoryEntryKind.Item, objects.Get("asteria:pebble").RewardEntry.Kind);
        Assert.Equal("asteria:pebble", objects.Get("asteria:pebble").RewardEntry.Id);
        Assert.Equal("asteria:water", objects.Get("asteria:lily_pad").SupportFluid);
        var rule = Assert.Single(biomes.Get("asteria:overworld/swamp").Decorations,
            d => d.Block == "asteria:lily_pad");
        Assert.True(rule.IsObject);
        Assert.Equal("asteria:water", rule.FluidRequirement!.Fluid);
        Assert.Equal(DecorationFluidRelation.Below, rule.FluidRequirement.Relation);
    }
}
