using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockTintOptOutTests
{
    [Fact]
    public void RemovedFantasyTreeLeavesHaveNoRemainingBlockDefinitions()
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "blocks");
        var registry = BlockRegistry.FromJson(
            Directory.EnumerateFiles(root, "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        foreach (var name in new[] { "leaf_mycelora", "leaf_gloamroot" })
        {
            Assert.False(File.Exists(Path.Combine(root, name + ".json")));
            Assert.False(registry.TryGetId("asteria:" + name, out _));
        }
    }

    [Fact]
    public void TintNoneDisablesBiomeRecoloringEvenWhenTextureIsDyable()
    {
        var definition = new BlockDefinition(
            "asteria:untinted_leaf",
            tint: BlockTint.None,
            textures: new BlockTextureSet(top:
            [
                new BlockTextureLayer("textures/test/leaf.png", dyable: true),
            ]),
            windSway: true);

        AssertWhiteVertexColorsAndWindOnlyLayerCode(definition);
    }

    private static void AssertWhiteVertexColorsAndWindOnlyLayerCode(BlockDefinition definition)
    {
        var texture = Assert.Single(definition.Textures.ResolveForFace(BlockFace.Top)).Texture;
        var blocks = new BlockRegistry([definition]);
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId(definition.Id));
        ChunkLightingSolver.Initialize(chunk, blocks, new FluidRegistry([]));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);

        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world,
            ChunkCoord.Zero,
            blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                [texture] = 1,
            }),
            meshletIndex: 0);

        var vertices = mesh.RenderBatches.SelectMany(batch => batch.Vertices).ToArray();
        Assert.NotEmpty(vertices);
        Assert.All(vertices, vertex =>
        {
            Assert.Equal(1f, vertex.TintAndAo.X);
            Assert.Equal(1f, vertex.TintAndAo.Y);
            Assert.Equal(1f, vertex.TintAndAo.Z);
            // 1 is the atlas layer; only the 0.5 wind bit may be set.
            // The 0.25 tint bit must stay clear.
            Assert.Equal(1.5f, vertex.EncodedTextureLayers.X);
            Assert.Equal(-1f, vertex.EncodedTextureLayers.Y);
        });
    }
}
