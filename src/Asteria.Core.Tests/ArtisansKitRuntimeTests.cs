using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ArtisansKitRuntimeTests
{
    private const string ToolId = "asteria:artisans_kit_rustic";
    private static readonly WorldVoxelCoord Position = new(2, 3, 4);
    private static readonly VoxelWorldHit Hit = new(Position, -1, 0, 0);
    private static readonly Vector3 Eye = new(0.5f, 3.5f, 4.5f);
    private static readonly Vector3 Direction = Vector3.UnitX;
    private static readonly WorldAabb ClearPlayer = new(
        new Vector3(0f, 0f, 0f), new Vector3(1f, 2f, 1f));

    private static (ArtisansKitRuntime Runtime, VoxelWorld World,
        VoxelMutationRuntime Mutations, BlockRegistry Blocks) Fixture()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", tags: ["fragmentable"]),
            new BlockDefinition("asteria:protected", tags: ["fragmentable"],
                mining: new BlockMiningDefinition(unbreakable: true)),
            new BlockDefinition("asteria:dirt"),
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var tools = PackContentRegistry<ToolDefinition>.FromJson([
            """
            {
              "id":"asteria:artisans_kit_rustic",
              "category":"tools",
              "icon":"textures/tools/artisans-kit.png",
              "leftBehavior":"asteria:artisans_kit/remove",
              "rightBehavior":"asteria:artisans_kit/restore"
            }
            """,
        ], ToolDefinition.Parse, definition => definition.Id);
        return (new ArtisansKitRuntime(world, blocks, mutations, tools),
            world, mutations, blocks);
    }

    private static InventoryStack Equipped() =>
        new(InventoryEntry.FromTool(ToolId));

    [Fact]
    public void RemoveThenRestorePreservesParentAndFullGeometry()
    {
        var (kit, world, mutations, blocks) = Fixture();
        var source = new VoxelCell(blocks.GetId("asteria:stone"),
            facing: HorizontalFacing.West, state: 7);
        Assert.True(mutations.SetCellAt(Position, source, out _));
        Assert.True(kit.IsEquipped(Equipped()));
        Assert.True(kit.TryEdit(
            Equipped(), ToolUseHand.Left, Hit, Eye, Direction, ClearPlayer));

        var partial = world.GetCellOrEmpty(Position);
        Assert.True(partial.HasMicroblockGeometry);
        Assert.Equal(source.Block, partial.Block);
        Assert.Equal((ushort)7, partial.State);
        Assert.Equal(HorizontalFacing.West, partial.Facing);
        Assert.Equal(448, world.GetMicroblockMaskOrEmpty(Position).OccupiedCount);

        Assert.True(kit.TryEdit(
            Equipped(), ToolUseHand.Right, Hit, Eye, Direction, ClearPlayer));
        var restored = world.GetCellOrEmpty(Position);
        Assert.Equal(source, restored);
        Assert.False(restored.HasMicroblockGeometry);
        Assert.False(kit.TryEdit(
            Equipped(), ToolUseHand.Right, Hit, Eye, Direction, ClearPlayer));
    }

    [Fact]
    public void CyclesExactlyThreeAuthoredResolutions()
    {
        var (kit, _, _, _) = Fixture();
        Assert.Equal(MicroblockResolution.Thick, kit.Resolution);
        Assert.Equal(MicroblockResolution.Thin, kit.CycleResolution());
        Assert.Equal(MicroblockResolution.ExtraThin, kit.CycleResolution());
        Assert.Equal(MicroblockResolution.Thick, kit.CycleResolution());
    }

    [Fact]
    public void RejectsRestorationIntersectingPlayerWithoutMutation()
    {
        var (kit, world, mutations, blocks) = Fixture();
        Assert.True(mutations.SetBlockAt(
            Position, blocks.GetId("asteria:stone"), out _));
        Assert.True(kit.TryEdit(
            Equipped(), ToolUseHand.Left, Hit, Eye, Direction, ClearPlayer));
        var before = world.GetMicroblockMaskOrEmpty(Position);
        // The camera ray enters the upper-right microblock octant:
        // test player intersection against that exact restored piece.
        var occupiedByPlayer = new WorldAabb(
            new Vector3(2f, 3.5f, 4.5f),
            new Vector3(2.5f, 4f, 5f));

        Assert.False(kit.TryEdit(
            Equipped(), ToolUseHand.Right, Hit, Eye, Direction, occupiedByPlayer));
        Assert.Equal(before, world.GetMicroblockMaskOrEmpty(Position));
    }

    [Fact]
    public void RejectsUnbreakableUnfragmentableAirAndUnownedTool()
    {
        var (kit, world, mutations, blocks) = Fixture();
        Assert.True(mutations.SetBlockAt(Position,
            blocks.GetId("asteria:protected"), out _));
        Assert.False(kit.TryEdit(Equipped(), ToolUseHand.Left,
            Hit, Eye, Direction, ClearPlayer));
        Assert.Equal(blocks.GetId("asteria:protected"),
            world.GetCellOrEmpty(Position).Block);

        Assert.True(mutations.SetBlockAt(Position,
            blocks.GetId("asteria:dirt"), out _));
        Assert.False(kit.TryEdit(Equipped(), ToolUseHand.Left,
            Hit, Eye, Direction, ClearPlayer));

        Assert.True(mutations.SetBlockAt(Position,
            blocks.GetId("asteria:stone"), out _));
        Assert.False(kit.TryEdit(null, ToolUseHand.Left,
            Hit, Eye, Direction, ClearPlayer));
        Assert.False(kit.TryEdit(
            new InventoryStack(InventoryEntry.FromTool("asteria:unowned")),
            ToolUseHand.Left, Hit, Eye, Direction, ClearPlayer));
        Assert.False(kit.TryEdit(Equipped(), ToolUseHand.Left,
            Hit with { NormalX = 0 }, Eye, Direction, ClearPlayer));

        Assert.True(mutations.SetCellAt(Position, VoxelCell.Empty, out _));
        Assert.False(kit.TryEdit(Equipped(), ToolUseHand.Left,
            Hit, Eye, Direction, ClearPlayer));
    }
}
