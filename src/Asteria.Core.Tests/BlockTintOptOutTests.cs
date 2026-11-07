using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockTintOptOutTests
{
    [Theory]
    [InlineData("leaf_mycelora")]
    [InlineData("leaf_gloamroot")]
    public void AuthoredUntintedLeavesKeepTheirOriginalColorsWithWindSway(string name)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", "blocks", name + ".json");
        var definition = BlockDefinitionJson.Parse(File.ReadAllText(path));

        Assert.Equal(BlockTint.None, definition.Tint);
        Assert.True(definition.WindSway);
        Assert.All(definition.Textures.AllLayers(), layer => Assert.False(layer.Dyable));
        AssertWhiteVertexColorsAndWindOnlyLayerCode(definition);
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
