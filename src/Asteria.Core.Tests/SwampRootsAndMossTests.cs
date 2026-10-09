using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SwampRootsAndMossTests
{
    [Fact]
    public void SwampRootsRespectNearbyWaterAndFavorWillowGrove()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateFluids(fluids);

        var swamp = biomes.Get("asteria:overworld/swamp");
        var roots = Assert.Single(swamp.Decorations,
            d => d.Block == "asteria:exposed_roots");
        Assert.Equal(DecorationFluidRelation.Nearby, roots.FluidRequirement!.Relation);
        Assert.Equal("asteria:water", roots.FluidRequirement.Fluid);
        Assert.Equal(3, roots.FluidRequirement.MaxDistance);
        Assert.Contains("asteria:mud", roots.SurfaceBlocks);
        Assert.True(roots.HabitatWeights!.For("willow_grove") >
                    roots.HabitatWeights.For("open_mire"));

        var visual = blocks.GetDefinition(blocks.GetId(roots.Block));
        Assert.Equal(BlockVisualKind.GroundSprite, visual.Visual.Kind);
        Assert.True(visual.HasTag(BlockPhysicsCapabilities.SupportBelow));
    }

    [Fact]
    public void WillowMossHangsOnlyFromAnAuthoredLeafAbove()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var mossId = "asteria:hanging_moss";
        var visual = blocks.GetDefinition(blocks.GetId(mossId));
        Assert.Equal(BlockVisualKind.CrossedSprite, visual.Visual.Kind);
        Assert.True(visual.HasTag(BlockPhysicsCapabilities.SupportAbove));
        Assert.False(visual.IsCollidable);
        Assert.True(visual.WindSway);

        var willows = structures.ResolveReference("asteria:tree_willow");
        Assert.Equal(3, willows.Count);
        foreach (var tree in willows)
        {
            var cells = tree.Voxels.ToDictionary(
                v => (v.X, v.Y, v.Z),
                v => v.Block);
            var hanging = tree.Voxels.Where(v => v.Block == mossId).ToArray();
            Assert.Equal(7, hanging.Length);
            foreach (var moss in hanging)
            {
                Assert.True(cells.TryGetValue(
                    (moss.X, moss.Y + 1, moss.Z),
                    out var support));
                Assert.Equal("asteria:leaf_willow", support);
                Assert.DoesNotContain(tree.Voxels, v =>
                    v.X == moss.X && v.Y == moss.Y && v.Z == moss.Z &&
                    v.Block != mossId);
            }
        }
    }

    [Fact]
    public void UpperSupportDetachesWhenLeafIsRemovedAndQueueWakesBelow()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:leaf_willow"),
            new BlockDefinition("asteria:hanging_moss",
                tags: [BlockPhysicsCapabilities.SupportAbove],
                isCollidable: false)
        ]);
        var mossId = blocks.GetId("asteria:hanging_moss");
        var leafId = blocks.GetId("asteria:leaf_willow");
        var chunk = new Chunk();
        chunk.SetBlock(4, 5, 4, mossId);
        chunk.SetBlock(4, 6, 4, leafId);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);

        var position = new WorldVoxelCoord(4, 5, 4);
        var definition = blocks.GetDefinition(mossId);
        var cell = chunk.GetCell(4, 5, 4);
        Assert.Equal(BlockSupportState.Supported,
            BlockSupportRules.Evaluate(world, blocks, definition, cell,
                world.GetMicroblockMaskOrEmpty(position), position));
        chunk.SetBlock(4, 6, 4, BlockRuntimeId.Air);
        Assert.Equal(BlockSupportState.Unsupported,
            BlockSupportRules.Evaluate(world, blocks, definition, cell,
                world.GetMicroblockMaskOrEmpty(position), position));

        var queue = new BlockPhysicsUpdateQueue();
        queue.EnqueueVoxelEdit(new WorldVoxelCoord(4, 6, 4));
        Assert.Contains(position, queue.DrainBatch());
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
