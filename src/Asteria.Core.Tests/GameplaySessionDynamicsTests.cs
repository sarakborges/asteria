using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GameplaySessionDynamicsTests
{
    private static DimensionDefinition Dimension(string id) => new(
        new DimensionId(id), [id + "/plain"], 90, 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0), new DimensionColor(255, 255, 255),
            1f, new DimensionColor(0, 0, 0), 0f));

    private static DimensionRegistry Dimensions() => new([
        Dimension("asteria:overworld"), Dimension("asteria:umbral")]);

    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content() => (
        new BlockRegistry([new BlockDefinition("asteria:stone")]),
        new FluidRegistry([]), new DyeRegistry([]),
        new AttachedLayerRegistry([]));

    private static GameplaySessionSnapshot Capture(
        DimensionSessionStateStore states,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        GameplaySessionSaveCodec.Capture(states,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    private static DimensionSessionStateStore Restore(
        WorldCreationOptions creation, GameplaySessionSnapshot saved,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        GameplaySessionSaveCodec.Restore(creation, Dimensions(), saved,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    [Fact]
    public void RuntimeDynamicsAndManualStructuresAreSphereIsolated()
    {
        var content = Content();
        var creation = new WorldCreationOptions("World", 79UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        var overworld = states.GetOrCreate(DimensionId.Overworld);
        var umbral = states.GetOrCreate(new DimensionId("asteria:umbral"));
        var footprint = new ManualStructurePlacementFootprint(
            "asteria:tree_oak", 4, 4, 4, 5, 12, 14, 4, 5);
        overworld.ManualStructures.RecordCommitted(footprint);

        var falling = new FallingBlockState(
            new FallingBlockId(1),
            BlockStateSnapshot.FromCell(
                new VoxelCell(content.Blocks.GetId("asteria:stone"))),
            4, 6, 18.25, -2.5);
        overworld.BlockPhysics = new BlockPhysicsRuntimeSnapshot(1, [falling]);
        var drop = new DroppedBlockState(
            new DroppedBlockId(1),
            new InventoryStack(InventoryEntry.FromItem("asteria:stick")),
            new Vector3(1, 6, 2), Vector3.UnitY, 4.5, false);
        overworld.DroppedBlocks = new DroppedBlockRuntimeSnapshot(1, [
            new DroppedBlockRuntimeEntry(drop, null)
        ]);
        var creature = new CreatureInstanceState(
            new CreatureInstanceId(1), "asteria:slime_aqua",
            new Vector3(5, 8, 3), 2f, 7.25);
        umbral.Creatures = new CreatureRuntimeSnapshot(1, [creature]);

        var saved = Capture(states, content);
        var restored = Restore(creation, saved, content);

        var first = restored.GetOrCreate(DimensionId.Overworld);
        var second = restored.GetOrCreate(new DimensionId("asteria:umbral"));
        Assert.Equal(footprint, Assert.Single(first.ManualStructures.Snapshot()));
        Assert.True(first.ManualStructures.Overlaps(footprint));
        Assert.Empty(second.ManualStructures.Snapshot());
        Assert.Equal(falling, Assert.Single(first.BlockPhysics!.ActiveBlocks));
        Assert.Equal(drop,
            Assert.Single(first.DroppedBlocks!.ActiveBlocks).State);
        Assert.Null(first.Creatures);
        Assert.Null(second.BlockPhysics);
        Assert.Null(second.DroppedBlocks);
        Assert.Equal(creature, Assert.Single(second.Creatures!.Creatures));
    }

    [Fact]
    public void CapturedEntityCollectionsDoNotExposeMutableArrays()
    {
        var content = Content();
        var creation = new WorldCreationOptions("World", 80UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        var sphere = states.GetOrCreate(DimensionId.Overworld);
        var creature = new CreatureInstanceState(
            new CreatureInstanceId(1), "asteria:slime_aqua",
            new Vector3(0, 12, 0), 5, 1);
        var entries = new [] {creature};
        sphere.Creatures = new CreatureRuntimeSnapshot(1, entries);
        var saved = Capture(states, content);
        var snapshot = saved.Spheres[0].Creatures!;
        entries[0] = creature with { Health = 0 };
        Assert.Equal(creature, Assert.Single(snapshot.Creatures));
        Assert.False(snapshot.Creatures is CreatureInstanceState[]);
        Assert.Equal(creature, Assert.Single(saved.Spheres[0].Creatures!.Creatures));
    }

    [Fact]
    public void BadDynamicsAndOverlappingStructureHistoryAreRejected()
    {
        var invalidDrop = new DroppedBlockState(
            new DroppedBlockId(1),
            new InventoryStack(InventoryEntry.FromItem("asteria:stick")),
            new Vector3(float.NaN, 3, 0), Vector3.Zero, 0, false);
        Assert.Throws<InvalidDataException>(() =>
            new SphereClockSnapshot(DimensionId.Overworld, 0, null, null, 0,
                droppedBlocks: new DroppedBlockRuntimeSnapshot(1, [
                    new DroppedBlockRuntimeEntry(invalidDrop, null)])));

        var repeated = new FallingBlockState(
            new FallingBlockId(1),
            BlockStateSnapshot.FromCell(new VoxelCell(new BlockRuntimeId(1))),
            0, 0, 2, 0);
        Assert.Throws<InvalidDataException>(() =>
            new SphereClockSnapshot(DimensionId.Overworld, 0, null, null, 0,
                blockPhysics: new BlockPhysicsRuntimeSnapshot(1,
                    [repeated, repeated])));

        var a = new ManualStructurePlacementFootprint(
            "asteria:a", 0, 0, 0, 5, 4, 7, 0, 5);
        var b = new ManualStructurePlacementFootprint(
            "asteria:b", 2, 2, 2, 7, 4, 7, 2, 7);
        var content = Content();
        var creation = new WorldCreationOptions("World", 90UL);
        var original = new DimensionSessionStateStore(creation, Dimensions());
        original.GetOrCreate(DimensionId.Overworld);
        var captured = Capture(original, content);
        var invalid = new GameplaySessionSnapshot(
            captured.Spatial, captured.Name,
            captured.TicksPerSecond, captured.SpawnCreatures,
            captured.Player,
            [new SphereClockSnapshot(DimensionId.Overworld,
                0, null, null, 0, manualStructures: [a, b])]);
        Assert.Throws<InvalidDataException>(() =>
            Restore(creation, invalid, content));
        Assert.Equal(0, original.GetOrCreate(
            DimensionId.Overworld).ManualStructures.Count);
    }
}
