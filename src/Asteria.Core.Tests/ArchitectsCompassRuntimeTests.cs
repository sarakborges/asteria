using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ArchitectsCompassRuntimeTests
{
    private static (
        ArchitectsCompassRuntime Runtime,
        VoxelWorld World,
        BlockRegistry Blocks) Setup()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:log", orientations: [
                BlockOrientation.X, BlockOrientation.Y, BlockOrientation.Z
            ])
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var tools = PackContentRegistry<ToolDefinition>.FromJson([
            """
            {
              "id":"asteria:architects_compass",
              "category":"creative_tools",
              "icon":"textures/tools/architects-compass.png",
              "leftBehavior":"asteria:none",
              "rightBehavior":"asteria:structure/select"
            }
            """
        ], ToolDefinition.Parse, x => x.Id);
        return (new ArchitectsCompassRuntime(world, blocks, tools), world, blocks);
    }

    [Fact]
    public void SelectionBoundsAreInclusiveSymmetricalAndBounded()
    {
        var a = new WorldVoxelCoord(8, 8, -9);
        var b = new WorldVoxelCoord(2, 4, -3);
        Assert.True(StructureSelectionBounds.TryCreate(a, b, out var bounds, out _));
        Assert.Equal(new WorldVoxelCoord(2, 4, -9), bounds.Minimum);
        Assert.Equal(new WorldVoxelCoord(8, 8, -3), bounds.Maximum);
        Assert.Equal(7 * 5 * 7, bounds.Volume);
        Assert.True(StructureSelectionBounds.TryCreate(b, a, out var reversed, out _));
        Assert.Equal(bounds, reversed);
        Assert.False(StructureSelectionBounds.TryCreate(
            new WorldVoxelCoord(0, -1, 0),
            new WorldVoxelCoord(1, 0, 1), out _, out _));
        Assert.False(StructureSelectionBounds.TryCreate(
            new WorldVoxelCoord(0, 0, 0),
            new WorldVoxelCoord(128, 0, 0), out _, out _));
        Assert.False(StructureSelectionBounds.TryCreate(
            new WorldVoxelCoord(0, 0, 0),
            new WorldVoxelCoord(127, 127, 1), out _, out _));
    }

    [Fact]
    public void ExportProducesValidParserCompatiblePaletteAndAirRows()
    {
        var (_, world, blocks) = Setup();
        Assert.True(world.SetCellAt(new WorldVoxelCoord(1, 1, 1),
            new VoxelCell(blocks.GetId("asteria:stone")), out _));
        Assert.True(world.SetCellAt(new WorldVoxelCoord(2, 1, 2),
            new VoxelCell(blocks.GetId("asteria:log"),
                orientation: BlockOrientation.X), out _));
        Assert.True(StructureSelectionBounds.TryCreate(
            new WorldVoxelCoord(1, 1, 1), new WorldVoxelCoord(2, 1, 2),
            out var bounds, out _));
        Assert.True(StructureSelectionExporter.TryExport(
            world, blocks, bounds, "asteria:structure_test",
            out var exported, out var error), error);
        Assert.NotNull(exported);
        Assert.Equal(4, exported!.Volume);
        Assert.Equal(2, exported.OccupiedBlocks);
        Assert.Contains("\"rows\"", exported.Json);
        var definition = StructureDefinitionJson.Parse(exported.Json);
        Assert.Equal("asteria:structure_test", definition.Id);
        Assert.False(definition.Rotation);
        Assert.False(definition.Locatable);
        Assert.Equal(2, definition.Voxels.Count);
        Assert.Contains(definition.Voxels,
            voxel => voxel.Block == "asteria:log" &&
                voxel.Orientation == BlockOrientation.X &&
                voxel.X == 1 && voxel.Z == 1);
    }

    [Fact]
    public void ExportRejectsMissingChunksAndAnyUnrepresentableSurfaceState()
    {
        var (_, world, blocks) = Setup();
        var p = new WorldVoxelCoord(1, 1, 1);
        Assert.True(world.SetCellAt(p,
            new VoxelCell(blocks.GetId("asteria:stone")), out _));
        Assert.True(StructureSelectionBounds.TryCreate(
            p, new WorldVoxelCoord(16, 1, 1), out var spanning, out _));
        Assert.False(StructureSelectionExporter.TryExport(
            world, blocks, spanning, "asteria:missing_chunk",
            out _, out var unloadedError));
        Assert.Contains("Unloaded", unloadedError);
        Assert.True(world.SetBlockSurfaceStateAt(p,
            new BlockSurfaceState("asteria:red"), out _));
        Assert.True(StructureSelectionBounds.TryCreate(
            p, p, out var single, out _));
        Assert.False(StructureSelectionExporter.TryExport(
            world, blocks, single, "asteria:with_dye",
            out _, out var tintError));
        Assert.Contains("state", tintError);
    }

    [Fact]
    public void StartPreviewCompleteAndResetOnSlotChange()
    {
        var (compass, world, blocks) = Setup();
        var held = new InventoryStack(
            InventoryEntry.FromTool("asteria:architects_compass"));
        var first = new VoxelWorldHit(new WorldVoxelCoord(1, 1, 1), 0, 0, -1);
        var second = new VoxelWorldHit(new WorldVoxelCoord(2, 1, 2), 0, 0, 1);
        Assert.True(world.SetCellAt(new WorldVoxelCoord(1, 1, 1),
            new VoxelCell(blocks.GetId("asteria:stone")), out _));
        Assert.True(world.SetCellAt(new WorldVoxelCoord(2, 1, 2),
            new VoxelCell(blocks.GetId("asteria:log")), out _));
        Assert.Equal(ArchitectsCompassResult.Started,
            compass.Select(held, 0, first, "asteria:first",
                out _, out _));
        Assert.True(compass.TryPreviewBounds(second, out var bounds));
        Assert.Equal(8, bounds.Volume);
        Assert.False(compass.ClearUnlessEquipped(held, 0));
        Assert.True(compass.ClearUnlessEquipped(held, 2));
        Assert.Null(compass.SelectionStart);

        Assert.Equal(ArchitectsCompassResult.Started,
            compass.Select(held, 2, first, "asteria:second", out _, out _));
        Assert.Equal(ArchitectsCompassResult.Exported,
            compass.Select(held, 2, second, "asteria:exported",
                out var exported, out var message));
        Assert.Equal(2, exported!.OccupiedBlocks);
        Assert.Contains("Exported", message);
        Assert.Null(compass.SelectionStart);
        Assert.False(compass.TryPreviewBounds(second, out _));
        Assert.Equal(ArchitectsCompassResult.Rejected,
            compass.Select(held, 2,
                new VoxelWorldHit(new WorldVoxelCoord(1, 1, 1), 0, 0, 0),
                "asteria:invalid", out _, out _));
    }

    [Fact]
    public void NonCompassToolAndInvalidNormalsCannotCreateSelection()
    {
        var (compass, _, _) = Setup();
        var rightClick = new VoxelWorldHit(
            new WorldVoxelCoord(1, 1, 1), 0, 1, 0);
        var wrongTool = new InventoryStack(
            InventoryEntry.FromTool("asteria:rustic_pickaxe"));
        Assert.False(compass.IsEquipped(wrongTool));
        Assert.Equal(ArchitectsCompassResult.Rejected,
            compass.Select(wrongTool, 0, rightClick,
                "asteria:wrong_tool", out _, out _));
        Assert.Equal(ArchitectsCompassResult.Rejected,
            compass.Select(new InventoryStack(
                    InventoryEntry.FromTool("asteria:architects_compass")),
                0,
                new VoxelWorldHit(new WorldVoxelCoord(1, 1, 1), 0, 0, 0),
                "asteria:invalid_normal", out _, out _));
        Assert.Null(compass.SelectionStart);
    }

    [Fact]
    public void AirOnlySelectionFailsWithoutClearingTheFirstCorner()
    {
        var (compass, _, _) = Setup();
        var held = new InventoryStack(
            InventoryEntry.FromTool("asteria:architects_compass"));
        var target = new VoxelWorldHit(new WorldVoxelCoord(0, 1, 1), 1, 0, 0);
        Assert.Equal(ArchitectsCompassResult.Started,
            compass.Select(held, 0, target, "asteria:test", out _, out _));
        Assert.Equal(ArchitectsCompassResult.Failed,
            compass.Select(held, 0, target, "asteria:empty", out _, out var error));
        Assert.Contains("no blocks", error);
        Assert.NotNull(compass.SelectionStart);
    }
}
