using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TreeMaterialContentTests
{
    [Theory]
    [InlineData("log_oak", "oak")]
    [InlineData("log_willow", "oak")]
    public void CarpenterStripsEveryAuthoredWoodFamilyInEveryOrientation(
        string stem, string material)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = CreateMutations(world);
        var tools = CreateTools(world, blocks, mutations);
        var held = new InventoryStack(
            InventoryEntry.FromTool("asteria:axe_carpenters_rustic"));
        var position = new WorldVoxelCoord(2, 3, 4);
        var hit = new VoxelWorldHit(position, 0, 1, 0);
        var natural = blocks.GetId("asteria:" + stem);
        var stripped = blocks.GetId("asteria:" + stem + "_stripped");

        Assert.All(blocks.GetDefinition(stripped).Textures.Left, layer =>
            Assert.Equal($"textures/blocks/trees/{material}/stripped.png",
                layer.Texture));

        foreach (var orientation in new[]
                 { BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X })
        {
            var source = new VoxelCell(natural, TextureRotation.Degrees90,
                orientation, HorizontalFacing.West, state: 3);
            Assert.True(mutations.SetCellAt(position, source, out _));

            Assert.True(tools.TryUse(held, ToolUseHand.Right, hit));
            Assert.Equal(new VoxelCell(stripped, source.TextureRotation,
                    source.Orientation, source.Facing, source.State),
                world.GetCellOrEmpty(position));
            Assert.False(tools.TryUse(held, ToolUseHand.Right, hit));
        }
    }

    [Theory]
    [InlineData("log_oak")]
    [InlineData("log_willow")]
    public void ExistingHollowFamiliesCanBeStrippedWithoutLosingTheirShape(
        string stem)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = CreateMutations(world);
        var tools = CreateTools(world, blocks, mutations);
        var held = new InventoryStack(
            InventoryEntry.FromTool("asteria:axe_carpenters_rustic"));
        var position = new WorldVoxelCoord(2, 3, 4);
        var hit = new VoxelWorldHit(position, 0, 1, 0);
        var natural = blocks.GetId("asteria:" + stem);

        foreach (var orientation in new[]
                 { BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X })
        {
            Assert.True(mutations.SetCellAt(position,
                new VoxelCell(natural, orientation: orientation), out _));
            Assert.True(tools.TryUse(held, ToolUseHand.Left, hit));
            Assert.Equal(blocks.GetId("asteria:" + stem + "_hollow"),
                world.GetCellOrEmpty(position).Block);
            Assert.True(tools.TryUse(held, ToolUseHand.Right, hit));
            var cell = world.GetCellOrEmpty(position);
            Assert.Equal(blocks.GetId("asteria:" + stem + "_stripped_hollow"),
                cell.Block);
            Assert.Equal(orientation, cell.Orientation);
            Assert.Equal(BlockShapeKind.Hollow,
                blocks.GetDefinition(cell.Block).Shape.Kind);
        }
    }

    [Fact]
    public void AllWoodyVegetationTemplatesContainActualTreeLeafVoxels()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var woods = blocks.AuthoredDefinitions()
            .Where(entry => entry.Definition.Variant is not null)
            .Select(entry => entry.Definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var leaves = blocks.AuthoredDefinitions()
            .Where(entry => entry.Definition.Category == "foliage" &&
                            entry.Definition.Visual.Kind == BlockVisualKind.Geometry &&
                            entry.Definition.Textures.AllLayers().Any(layer =>
                                layer.Texture.StartsWith("textures/blocks/trees/",
                                    StringComparison.Ordinal)))
            .Select(entry => entry.Definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var vegetation = structures.Definitions()
            .Where(structure => structure.Voxels.Any(voxel =>
                woods.Contains(voxel.Block)))
            .ToArray();

        Assert.NotEmpty(vegetation);
        Assert.All(vegetation, structure =>
            Assert.True(structure.Voxels.Any(voxel => leaves.Contains(voxel.Block)),
                $"{structure.Id} must retain visible fantasy foliage."));

    }

    private static VoxelMutationRuntime CreateMutations(VoxelWorld world) =>
        new(world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());

    private static ToolGameplayRuntime CreateTools(
        VoxelWorld world, BlockRegistry blocks, VoxelMutationRuntime mutations) =>
        new(world, blocks, mutations,
            PackContentRegistry<ToolDefinition>.FromJson(ReadJson("tools"),
                ToolDefinition.Parse, tool => tool.Id));

    private static IEnumerable<string> ReadJson(string directory) =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", directory), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
