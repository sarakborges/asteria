using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SubmergedObjectLifecycleTests
{
    private static readonly string[] Documents =
    [
        """
        {"id":"asteria:seagrass","category":"foliage",
         "icon":"textures/objects/seagrass.png","dropSelf":true,
         "placementFaces":["top"],"requiredFluidAbove":"asteria:water",
         "visual":{"type":"crossedSprite","texture":"textures/objects/seagrass.png",
                   "width":0.8,"height":0.78,"planes":4}}
        """,
        """
        {"id":"asteria:anemone_sea","category":"foliage",
         "icon":"textures/objects/anemone_sea.png","dropSelf":false,
         "placementFaces":["top"],"requiredFluidAbove":"asteria:water",
         "visual":{"type":"crossedSprite","texture":"textures/objects/anemone_sea.png",
                   "width":0.8,"height":0.88,"planes":4}}
        """
    ];

    private static (VoxelWorld World, VoxelMutationRuntime Mutations,
        DroppedBlockRuntime Drops, BlockRegistry Blocks, FluidRegistry Fluids,
        PackContentRegistry<ObjectDefinition> Objects)
        Setup(int capacity = 16, bool crossChunk = false)
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), .7f),
            new FluidDefinition("asteria:lava", new FluidColor(255, 64, 0), .7f)
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Documents, ObjectDefinition.Parse, value => value.Id);
        var world = new VoxelWorld();
        world.ConfigureFluidOccupancy(blocks);
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        if (crossChunk)
            world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks, maximumActive: capacity);
        mutations.ConfigureObjectDetachment(
            new ObjectDetachmentRuntime(world, objects, drops, fluids));
        return (world, mutations, drops, blocks, fluids, objects);
    }

    [Fact]
    public void ManualPlacementRequiresMatchingWaterAndPreservesSource()
    {
        var (world, mutations, _, blocks, fluids, definitions) = Setup();
        var support = new WorldVoxelCoord(2, 3, 2);
        var target = support + (0, 1, 0);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        var held = new InventoryStack(InventoryEntry.FromObject("asteria:seagrass"));
        var hit = new VoxelWorldHit(support, 0, 1, 0);
        Assert.False(ObjectPlacementResolver.Resolve(
            world, blocks, definitions, held, hit, fluids).Accepted);
        mutations.SetFluidAt(target,
            FluidCell.Source(fluids.GetId("asteria:lava")), out _);
        Assert.False(ObjectPlacementResolver.Resolve(
            world, blocks, definitions, held, hit, fluids).Accepted);

        var source = FluidCell.Source(fluids.GetId("asteria:water"));
        mutations.SetFluidAt(target, source, out _);
        var decision = ObjectPlacementResolver.Resolve(
            world, blocks, definitions, held, hit, fluids);
        Assert.True(decision.Accepted);
        Assert.True(mutations.AddObjectAt(decision.Support, decision.Object));
        Assert.Equal(source, world.GetFluidOrEmpty(target));
        Assert.True(world.GetCellOrEmpty(target).IsEmpty);
    }

    [Fact]
    public void FluidIdentityChangeDetachesDroppingAndNonDroppingPlants()
    {
        var (world, mutations, drops, blocks, fluids, _) = Setup();
        var support = new WorldVoxelCoord(2, 3, 2);
        var target = support + (0, 1, 0);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        var water = fluids.GetId("asteria:water");
        mutations.SetFluidAt(target, FluidCell.Source(water), out _);
        mutations.AddObjectAt(support,
            new ObjectCell("asteria:seagrass", BlockFace.Top));
        mutations.AddObjectAt(support,
            new ObjectCell("asteria:anemone_sea", BlockFace.Top));

        Assert.True(mutations.SetFluidAt(target,
            FluidCell.Spreading(water, 4, 2), out _));
        Assert.Equal(2, world.TryGetObjects(support, out var wet)
            ? wet.Count : 0);
        Assert.Empty(drops.ActiveBlocks);

        Assert.True(mutations.SetFluidAt(target,
            FluidCell.Source(fluids.GetId("asteria:lava")), out _));
        Assert.Empty(world.TryGetObjects(support, out var remaining)
            ? remaining : []);
        Assert.Equal("asteria:seagrass",
            Assert.Single(drops.ActiveBlocks).Stack.Id);
    }

    [Fact]
    public void WaterRemovalAcrossVerticalChunkBoundaryDetachesTheObject()
    {
        var (world, mutations, drops, blocks, fluids, _) =
            Setup(crossChunk: true);
        var support = new WorldVoxelCoord(4, Chunk.Size - 1, 4);
        var target = support + (0, 1, 0);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        mutations.SetFluidAt(target,
            FluidCell.Source(fluids.GetId("asteria:water")), out _);
        mutations.AddObjectAt(support,
            new ObjectCell("asteria:seagrass", BlockFace.Top));
        Assert.True(mutations.SetFluidAt(target, FluidCell.Empty, out _));
        Assert.Empty(world.TryGetObjects(support, out var remaining)
            ? remaining : []);
        Assert.Equal("asteria:seagrass",
            Assert.Single(drops.ActiveBlocks).Stack.Id);
    }

    [Fact]
    public void FluidBatchPreflightsAllDropsWithoutPartialMutation()
    {
        var (world, mutations, drops, blocks, fluids, _) = Setup(capacity: 1);
        var water = FluidCell.Source(fluids.GetId("asteria:water"));
        var first = new WorldVoxelCoord(1, 3, 2);
        var second = new WorldVoxelCoord(4, 3, 2);
        foreach (var support in new[] { first, second })
        {
            mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
            mutations.SetFluidAt(support + (0, 1, 0), water, out _);
            mutations.AddObjectAt(support,
                new ObjectCell("asteria:seagrass", BlockFace.Top));
        }
        var revision = world.Revision;
        var changes = new[]
        {
            new FluidCellChange(first + (0, 1, 0), water, FluidCell.Empty),
            new FluidCellChange(second + (0, 1, 0), water, FluidCell.Empty)
        };
        Assert.False(mutations.ApplyFluidChanges(changes).Accepted);
        Assert.Equal(revision, world.Revision);
        Assert.Empty(drops.ActiveBlocks);
        foreach (var support in new[] { first, second })
        {
            Assert.Equal(water, world.GetFluidOrEmpty(support + (0, 1, 0)));
            Assert.Single(world.TryGetObjects(support, out var resident)
                ? resident : []);
        }
        mutations.RemoveObjectAt(second,
            new ObjectCell("asteria:seagrass", BlockFace.Top));
        var accepted = mutations.ApplyFluidChanges(changes);
        Assert.True(accepted.Accepted);
        Assert.Equal(2, accepted.AppliedChangeCount);
        Assert.Equal("asteria:seagrass",
            Assert.Single(drops.ActiveBlocks).Stack.Id);
    }

    [Fact]
    public void SolidReplacementPreflightsObjectDropsBeforeWaterDisplacement()
    {
        var (world, mutations, drops, blocks, fluids, _) = Setup(capacity: 1);
        var support = new WorldVoxelCoord(3, 3, 3);
        var target = support + (0, 1, 0);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        var water = FluidCell.Source(fluids.GetId("asteria:water"));
        mutations.SetFluidAt(target, water, out _);
        mutations.AddObjectAt(support,
            new ObjectCell("asteria:seagrass", BlockFace.Top));
        drops.Spawn(new InventoryStack(InventoryEntry.FromItem("asteria:occupied")),
            new Vector3(8, 8, 8));
        var revision = world.Revision;
        Assert.False(mutations.SetBlockAt(target, blocks.GetId("asteria:stone"),
            out _));
        Assert.Equal(revision, world.Revision);
        Assert.Equal(water, world.GetFluidOrEmpty(target));
        Assert.Single(world.TryGetObjects(support, out var resident)
            ? resident : []);
    }

    [Fact]
    public void DefaultOceanObjectsAllRequireTargetWater()
    {
        var folder = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "objects");
        var all = PackContentRegistry<ObjectDefinition>.FromJson(
            Directory.EnumerateFiles(folder, "*.json").Select(File.ReadAllText),
            ObjectDefinition.Parse, obj => obj.Id);
        foreach (var name in new[] {
            "kelp", "seagrass", "anemone_sea", "seashell", "starfish"
        })
        {
            var obj = all.Get("asteria:" + name);
            Assert.Equal("asteria:water", obj.RequiredFluidAbove);
            Assert.Null(obj.SupportFluid);
            Assert.Equal(name != "anemone_sea", obj.DropSelf);
        }
    }

    [Fact]
    public void SubmergedPolicyRejectsIncompatibleSupportConfiguration()
    {
        const string template = """
            {"id":"asteria:test_flora","category":"foliage",
             "icon":"textures/objects/seagrass.png",
             "visual":{"type":"crossedSprite",
                       "texture":"textures/objects/seagrass.png"},
             "requiredFluidAbove":"asteria:water",
             "placementFaces":["top"]}
            """;
        Assert.Equal("asteria:water",
            ObjectDefinition.Parse(template).RequiredFluidAbove);
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            template.Replace("[\"top\"]", "[\"front\"]")));
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            template.Replace("\"requiredFluidAbove\"",
                "\"supportFluid\":\"asteria:water\",\"requiredFluidAbove\"")));
    }
}
