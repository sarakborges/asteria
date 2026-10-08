using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockMiningRuntimeTests
{
    private sealed record Fixture(
        BlockMiningRuntime Mining,
        ToolGameplayRuntime Tools,
        BlockRegistry Blocks,
        VoxelWorld World,
        VoxelMutationRuntime Mutations,
        DroppedBlockRuntime Drops);

    private static Fixture CreateFixture()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", mining:
                new BlockMiningDefinition(hardness: 2f,
                    requiredTools: ["pickaxe"])),
            new BlockDefinition("asteria:dirt", mining:
                new BlockMiningDefinition(hardness: 1f,
                    preferredTools: ["shovel"])),
            new BlockDefinition("asteria:leaves", mining:
                new BlockMiningDefinition(hardness: 0f)),
            new BlockDefinition("asteria:sphere_shell", mining:
                new BlockMiningDefinition(unbreakable: true)),
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops);
        var tools = PackContentRegistry<ToolDefinition>.FromJson([
            """
            {
              "id":"asteria:pickaxe_rustic",
              "category":"tools",
              "icon":"textures/tools/pickaxe-rustic.png",
              "leftBehavior":"asteria:mine",
              "rightBehavior":"asteria:none",
              "mining":{"category":"pickaxe","speed":1.5}
            }
            """,
            """
            {
              "id":"asteria:shovel_rustic",
              "category":"tools",
              "icon":"textures/tools/shovel-rustic.png",
              "leftBehavior":"asteria:mine",
              "rightBehavior":"asteria:none",
              "mining":{"category":"shovel","speed":1.5}
            }
            """,
            """
            {
              "id":"asteria:carpenters_axe_rustic",
              "category":"tools",
              "icon":"textures/tools/carpenters-axe-rustic.png",
              "leftBehavior":"asteria:log/hollow",
              "rightBehavior":"asteria:log/strip"
            }
            """
        ], ToolDefinition.Parse, item => item.Id);
        var toolRuntime = new ToolGameplayRuntime(
            world, blocks, mutations, tools);
        return new Fixture(
            new BlockMiningRuntime(world, blocks, toolRuntime, interactions),
            toolRuntime, blocks, world, mutations, drops);
    }

    private static readonly WorldVoxelCoord First = new(2, 3, 4);
    private static readonly WorldVoxelCoord Second = new(3, 3, 4);

    private static VoxelWorldHit Hit(WorldVoxelCoord voxel) =>
        new(voxel, 0, 1, 0);

    private static InventoryStack Tool(string id) =>
        new(InventoryEntry.FromTool(id));

    [Fact]
    public void RequiredPickaxeAccumulatesAuthoredWorkAndDropsOnce()
    {
        var f = CreateFixture();
        var stone = f.Blocks.GetId("asteria:stone");
        Assert.True(f.Mutations.SetBlockAt(First, stone, out _));
        var pickaxe = Tool("asteria:pickaxe_rustic");
        Assert.Null(f.Tools.EffectiveMiningSpeed(
            null, f.Blocks.GetDefinition(stone)));
        Assert.Equal(1.5f, f.Tools.EffectiveMiningSpeed(
            pickaxe, f.Blocks.GetDefinition(stone)));

        Assert.False(f.Mining.Advance(
            Hit(First), pickaxe, 0, 266, PlayerGameMode.Survival));
        Assert.InRange(f.Mining.Progress!.Value, 0.99f, 1f);
        Assert.Equal(stone, f.World.GetCellOrEmpty(First).Block);
        Assert.True(f.Mining.Advance(
            Hit(First), pickaxe, 0, 1, PlayerGameMode.Survival));
        Assert.True(f.World.GetCellOrEmpty(First).IsEmpty);
        Assert.Null(f.Mining.Progress);
        Assert.Single(f.Drops.ActiveBlocks);
        Assert.False(f.Mining.Advance(
            Hit(First), pickaxe, 0, 1, PlayerGameMode.Survival));
        Assert.Single(f.Drops.ActiveBlocks);
    }

    [Fact]
    public void PreferredShovelAcceleratesButHandStillMines()
    {
        var f = CreateFixture();
        var dirt = f.Blocks.GetId("asteria:dirt");
        Assert.True(f.Mutations.SetBlockAt(First, dirt, out _));
        var definition = f.Blocks.GetDefinition(dirt);
        var shovel = Tool("asteria:shovel_rustic");
        Assert.Equal(1f, f.Tools.EffectiveMiningSpeed(null, definition));
        Assert.Equal(1.5f, f.Tools.EffectiveMiningSpeed(shovel, definition));
        Assert.False(f.Mining.Advance(Hit(First), shovel, 0, 133,
            PlayerGameMode.Survival));
        Assert.True(f.Mining.Advance(Hit(First), shovel, 0, 1,
            PlayerGameMode.Survival));
        Assert.True(f.World.GetCellOrEmpty(First).IsEmpty);
    }

    [Fact]
    public void CancelsOnReleaseTargetToolSlotAndVoxelChange()
    {
        var f = CreateFixture();
        var dirt = f.Blocks.GetId("asteria:dirt");
        Assert.True(f.Mutations.SetBlockAt(First, dirt, out _));
        Assert.True(f.Mutations.SetBlockAt(Second, dirt, out _));
        Assert.False(f.Mining.Advance(Hit(First), null, 0, 150,
            PlayerGameMode.Survival));
        Assert.InRange(f.Mining.Progress!.Value, 0.74f, 0.76f);
        f.Mining.Cancel();
        Assert.Null(f.Mining.Progress);
        Assert.False(f.Mining.Advance(Hit(First), null, 0, 100,
            PlayerGameMode.Survival));

        Assert.False(f.Mining.Advance(Hit(Second), null, 0, 100,
            PlayerGameMode.Survival));
        Assert.False(f.Mining.Advance(Hit(Second), null, 1, 100,
            PlayerGameMode.Survival));
        Assert.False(f.Mining.Advance(
            Hit(Second), Tool("asteria:shovel_rustic"), 1, 100,
            PlayerGameMode.Survival));
        // Changes to voxel state cannot continue accumulated work.
        Assert.True(f.Mutations.SetCellAt(
            Second, new VoxelCell(dirt, state: 7), out _));
        Assert.False(f.Mining.Advance(
            Hit(Second), Tool("asteria:shovel_rustic"), 1, 100,
            PlayerGameMode.Survival));
        Assert.Equal(dirt, f.World.GetCellOrEmpty(Second).Block);
        Assert.False(f.Mining.Advance(null, null, 0, 200,
            PlayerGameMode.Survival));
        Assert.Null(f.Mining.Progress);
    }

    [Fact]
    public void InvalidToolModeAndProtectedBlocksNeverBreak()
    {
        var f = CreateFixture();
        var stone = f.Blocks.GetId("asteria:stone");
        var shell = f.Blocks.GetId("asteria:sphere_shell");
        Assert.True(f.Mutations.SetBlockAt(First, stone, out _));
        Assert.True(f.Mutations.SetBlockAt(Second, shell, out _));
        Assert.False(f.Mining.Advance(Hit(First), null, 0, 1000,
            PlayerGameMode.Survival));
        Assert.False(f.Mining.Advance(Hit(First),
            Tool("asteria:carpenters_axe_rustic"), 0, 1000,
            PlayerGameMode.Survival));
        Assert.False(f.Mining.Advance(Hit(First),
            Tool("asteria:pickaxe_rustic"), 0, 1000,
            PlayerGameMode.Spectator));
        Assert.False(f.Mining.Advance(Hit(Second), null, 0, 1000,
            PlayerGameMode.Survival));
        Assert.Equal(stone, f.World.GetCellOrEmpty(First).Block);
        Assert.Equal(shell, f.World.GetCellOrEmpty(Second).Block);
        Assert.Empty(f.Drops.ActiveBlocks);
    }

    [Fact]
    public void ZeroHardnessBreaksWithoutElapsedTicks()
    {
        var f = CreateFixture();
        Assert.True(f.Mutations.SetBlockAt(
            First, f.Blocks.GetId("asteria:leaves"), out _));
        Assert.True(f.Mining.Advance(Hit(First), null, 0, 0,
            PlayerGameMode.Survival));
        Assert.True(f.World.GetCellOrEmpty(First).IsEmpty);
    }
}
