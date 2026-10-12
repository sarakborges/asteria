using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SaplingGrowthRuntimeTests
{
    private const string SaplingJson = """
        {"id":"asteria:test_sapling","category":"foliage",
         "icon":"textures/objects/grass.png",
         "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png"},
         "growthStructure":"asteria:test_mature"}
        """;

    private static (VoxelWorld World, VoxelMutationRuntime Mutations,
        SaplingGrowthRuntime Growth, BlockRegistry Blocks)
        Setup(bool tallTree = false, int supportY = 3)
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:log"),
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            [SaplingJson], ObjectDefinition.Parse, obj => obj.Id);
        var voxels = new List<StructureVoxelDefinition>
        {
            new(0, 1, 0, "asteria:log", BlockOrientation.Y)
        };
        if (tallTree)
            voxels.Add(new StructureVoxelDefinition(
                0, 2, 0, "asteria:log", BlockOrientation.Y));
        var structures = new StructureRegistry([
            new StructureDefinition("asteria:test_mature", false,
                new StructureAnchor(0, 0, 0), voxels)
        ]);
        var world = new VoxelWorld();
        world.ConfigureFluidOccupancy(blocks);
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        mutations.ConfigureObjectDetachment(
            new ObjectDetachmentRuntime(world, objects, drops));
        var growth = new SaplingGrowthRuntime(
            world, mutations, blocks, objects, structures, seed: 18);
        var support = new WorldVoxelCoord(4, supportY, 4);
        Assert.True(mutations.SetBlockAt(
            support, blocks.GetId("asteria:stone"), out _));
        Assert.True(mutations.AddObjectAt(support,
            new ObjectCell("asteria:test_sapling", BlockFace.Top)));
        return (world, mutations, growth, blocks);
    }

    [Fact]
    public void SuccessfulGrowthConsumesObjectAndCommitsMatureBlock()
    {
        var (world, _, growth, blocks) = Setup();
        var support = new WorldVoxelCoord(4, 3, 4);
        var grown = false;
        for (ulong pulse = 1; pulse <= 256 && !grown; pulse++)
            grown = growth.Advance(pulse * SaplingGrowthRuntime.CheckPeriodTicks);
        Assert.True(grown, "A seeded sapling must eventually grow.");
        Assert.Empty(world.TryGetObjects(support, out var saplings)
            ? saplings : []);
        Assert.Equal(blocks.GetId("asteria:log"),
            world.GetCellOrEmpty(support + (0, 1, 0)).Block);
        Assert.Equal(blocks.GetId("asteria:stone"),
            world.GetCellOrEmpty(support).Block);
    }

    [Fact]
    public void TreeNeverOverwritesAnOccupiedTargetOrConsumesBlockedSapling()
    {
        var (world, mutations, growth, blocks) = Setup();
        var support = new WorldVoxelCoord(4, 3, 4);
        var trunk = support + (0, 1, 0);
        mutations.SetBlockAt(trunk, blocks.GetId("asteria:stone"), out _);
        for (ulong pulse = 1; pulse <= 128; pulse++)
            Assert.False(growth.Advance(
                pulse * SaplingGrowthRuntime.CheckPeriodTicks));
        Assert.Equal(blocks.GetId("asteria:stone"),
            world.GetCellOrEmpty(trunk).Block);
        Assert.Single(world.TryGetObjects(support, out var remaining)
            ? remaining : []);

        mutations.SetCellAt(trunk, VoxelCell.Empty, out _);
        var grown = false;
        for (ulong pulse = 129; pulse <= 512 && !grown; pulse++)
            grown = growth.Advance(
                pulse * SaplingGrowthRuntime.CheckPeriodTicks);
        Assert.True(grown);
    }

    [Fact]
    public void GrowthDefersUntilEveryTargetChunkIsResident()
    {
        var (world, _, growth, blocks) = Setup(tallTree: true, supportY: Chunk.Size - 1);
        var support = new WorldVoxelCoord(4, Chunk.Size - 1, 4);
        for (ulong pulse = 1; pulse <= 128; pulse++)
            Assert.False(growth.Advance(
                pulse * SaplingGrowthRuntime.CheckPeriodTicks));
        Assert.Single(world.TryGetObjects(support, out var waiting)
            ? waiting : []);
        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        var grown = false;
        for (ulong pulse = 129; pulse <= 512 && !grown; pulse++)
            grown = growth.Advance(
                pulse * SaplingGrowthRuntime.CheckPeriodTicks);
        Assert.True(grown);
        Assert.Equal(blocks.GetId("asteria:log"),
            world.GetCellOrEmpty(support + (0, 1, 0)).Block);
        Assert.Equal(blocks.GetId("asteria:log"),
            world.GetCellOrEmpty(support + (0, 2, 0)).Block);
    }

    [Fact]
    public void GrowthPausesWhileGameplayIsDisabledAndResumesDeterministically()
    {
        var (world, _, growth, _) = Setup();
        for (ulong pulse = 1; pulse <= 32; pulse++)
            Assert.False(growth.Advance(
                pulse * SaplingGrowthRuntime.CheckPeriodTicks,
                enabled: false));
        Assert.Single(world.TryGetObjects(
            new WorldVoxelCoord(4, 3, 4), out var waiting) ? waiting : []);
        var grown = false;
        for (ulong pulse = 33; pulse <= 256 && !grown; pulse++)
            grown = growth.Advance(
                pulse * SaplingGrowthRuntime.CheckPeriodTicks);
        Assert.True(grown);
    }

    [Fact]
    public void DefaultPackProvidesARealMatureTreeForEverySapling()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");
        IEnumerable<string> Docs(string folder) =>
            Directory.EnumerateFiles(Path.Combine(root, folder), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText);

        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Docs("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var structures = StructureRegistry.FromJson(Docs("structures"));
        var blocks = BlockRegistry.FromJson(Docs("blocks"));
        foreach (var species in new[] {
            "charwood", "enchanted_tree", "frost_wood", "juniper", "oak",
            "rime_deadwood", "skywood", "thornwood", "willow", "windwood", "world_tree"
        })
        {
            var sapling = objects.Get("asteria:sapling_" + species);
            Assert.NotNull(sapling.GrowthStructure);
            var variants = structures.ResolveReference(sapling.GrowthStructure!);
            Assert.NotEmpty(variants);
            Assert.All(variants, variant =>
            {
                Assert.Contains(variant.Voxels, v =>
                    v.X == variant.Anchor.X && v.Y == variant.Anchor.Y + 1 &&
                    v.Z == variant.Anchor.Z);
                Assert.All(variant.Voxels, v =>
                    Assert.True(blocks.TryGetId(v.Block, out _)));
            });
        }
        var empty = new VoxelWorld();
        var mutations = new VoxelMutationRuntime(
            empty, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        _ = new SaplingGrowthRuntime(empty, mutations, blocks, objects,
            structures, 5);
    }

    [Fact]
    public void MissingAdultStructureFailsAtStartupNotWhileWorldIsRunning()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            [SaplingJson], ObjectDefinition.Parse, obj => obj.Id);
        var world = new VoxelWorld();
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        Assert.Throws<ArgumentException>(() => new SaplingGrowthRuntime(
            world, mutations, blocks, objects, StructureRegistry.Empty, 1));
    }
}
