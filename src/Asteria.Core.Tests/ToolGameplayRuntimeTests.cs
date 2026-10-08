using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ToolGameplayRuntimeTests
{
    private static PackContentRegistry<ToolDefinition> CreateTools() =>
        PackContentRegistry<ToolDefinition>.FromJson([
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
              "id":"asteria:carpenters_axe_rustic",
              "category":"tools",
              "icon":"textures/tools/carpenters-axe-rustic.png",
              "leftBehavior":"asteria:log/hollow",
              "rightBehavior":"asteria:log/strip"
            }
            """
        ], ToolDefinition.Parse, x => x.Id);

    private static (
        ToolGameplayRuntime Tools,
        BlockRegistry Blocks,
        VoxelWorld World,
        VoxelMutationRuntime Mutations) Setup()
    {
        var family = "asteria:log_oak";
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", mining:
                new BlockMiningDefinition(2f, requiredTools: ["pickaxe"])),
            new BlockDefinition("asteria:log_oak", variant:
                new BlockVariantDefinition(family, "natural")),
            new BlockDefinition("asteria:log_oak_hollow", variant:
                new BlockVariantDefinition(family, "hollow")),
            new BlockDefinition("asteria:log_oak_stripped", variant:
                new BlockVariantDefinition(family, "stripped")),
            new BlockDefinition("asteria:log_oak_stripped_hollow", variant:
                new BlockVariantDefinition(family, "stripped_hollow")),
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        return (new ToolGameplayRuntime(world, blocks, mutations, CreateTools()),
            blocks, world, mutations);
    }

    [Fact]
    public void SurvivalRequiresMatchingAuthoredToolCategory()
    {
        var (tools, blocks, _, _) = Setup();
        var stone = blocks.GetDefinition(blocks.GetId("asteria:stone"));
        var pickaxe = new InventoryStack(
            InventoryEntry.FromTool("asteria:pickaxe_rustic"));
        var carpenter = new InventoryStack(
            InventoryEntry.FromTool("asteria:carpenters_axe_rustic"));
        Assert.False(tools.CanMine(null, stone, PlayerGameMode.Survival));
        Assert.False(tools.CanMine(carpenter, stone, PlayerGameMode.Survival));
        Assert.True(tools.CanMine(pickaxe, stone, PlayerGameMode.Survival));
        Assert.True(tools.CanMine(null, stone, PlayerGameMode.Creative));
        Assert.False(tools.CanMine(pickaxe, stone, PlayerGameMode.Spectator));
        var dirt = new BlockDefinition("asteria:dirt");
        Assert.True(tools.CanMine(null, dirt, PlayerGameMode.Survival));
        Assert.False(tools.CanMine(carpenter, dirt, PlayerGameMode.Survival));
    }

    [Fact]
    public void CarpenterActionsTransformRealVariantsAndPreserveVoxelState()
    {
        var (tools, blocks, world, mutations) = Setup();
        var pos = new WorldVoxelCoord(2, 3, 4);
        var source = new VoxelCell(
            blocks.GetId("asteria:log_oak"), facing: HorizontalFacing.West,
            state: 3);
        Assert.True(mutations.SetCellAt(pos, source, out _));
        var held = new InventoryStack(
            InventoryEntry.FromTool("asteria:carpenters_axe_rustic"));
        var hit = new VoxelWorldHit(pos, 0, 1, 0);

        Assert.True(tools.IsSpecialLeftAction(held));
        Assert.True(tools.TryUse(held, ToolUseHand.Right, hit));
        var stripped = world.GetCellOrEmpty(pos);
        Assert.Equal(blocks.GetId("asteria:log_oak_stripped"), stripped.Block);
        Assert.Equal(HorizontalFacing.West, stripped.Facing);
        Assert.Equal((ushort)3, stripped.State);

        Assert.True(tools.TryUse(held, ToolUseHand.Left, hit));
        var hollow = world.GetCellOrEmpty(pos);
        Assert.Equal(
            blocks.GetId("asteria:log_oak_stripped_hollow"), hollow.Block);
        Assert.Equal((ushort)3, hollow.State);
        Assert.False(tools.TryUse(held, ToolUseHand.Right, hit));
        Assert.False(tools.TryUse(held, ToolUseHand.Left, hit));
    }

    [Fact]
    public void UnauthoredTargetsNeverMutate()
    {
        var (tools, blocks, world, mutations) = Setup();
        var pos = new WorldVoxelCoord(2, 3, 4);
        var stone = blocks.GetId("asteria:stone");
        Assert.True(mutations.SetBlockAt(pos, stone, out _));
        var held = new InventoryStack(
            InventoryEntry.FromTool("asteria:carpenters_axe_rustic"));
        Assert.False(tools.TryUse(held, ToolUseHand.Right,
            new VoxelWorldHit(pos, 0, 1, 0)));
        Assert.Equal(stone, world.GetCellOrEmpty(pos).Block);
        Assert.False(tools.TryUse(held, ToolUseHand.Right,
            new VoxelWorldHit(new WorldVoxelCoord(200, 3, 4), 0, 1, 0)));
    }
}
