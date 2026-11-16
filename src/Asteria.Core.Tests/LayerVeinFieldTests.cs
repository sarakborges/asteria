using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class LayerVeinFieldTests
{
    private static BlockRegistry Blocks() => new([
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:basalt"),
        new BlockDefinition("asteria:dirt"),
        new BlockDefinition("asteria:shell_sphere",
            mining: new BlockMiningDefinition(unbreakable: true))
    ]);

    private static AttachedLayerRegistry Layers() => AttachedLayerRegistry.FromJson([
        """
        {"id":"asteria:ore_iron","texture":"textures/layers/ores/ore_iron.png",
         "scope":"voxel","mining":{"hardness":2,"requiredTools":["pickaxe"]},
         "drops":[{"kind":"item","id":"asteria:ore_iron_raw"}],
         "generation":{"dimension":"asteria:overworld",
           "hostBlocks":["asteria:stone","asteria:basalt"],
           "minY":3,"maxY":80,"spacing":8,"radius":3,"chance":1}}
        """
    ]);

    private static Chunk SolidChunk(BlockRuntimeId id)
    {
        var chunk = new Chunk();
        for (var z = 0; z < Chunk.Size; z++)
        for (var y = 0; y < Chunk.Size; y++)
        for (var x = 0; x < Chunk.Size; x++)
            chunk.SetBlock(x, y, z, id);
        return chunk;
    }

    private static int OreCount(Chunk chunk) =>
        Enumerable.Range(0, Chunk.Size).Sum(z =>
            Enumerable.Range(0, Chunk.Size).Sum(y =>
                Enumerable.Range(0, Chunk.Size).Count(x =>
                    chunk.GetSurfaceState(x, y, z).Layers.Count > 0)));

    [Fact]
    public void VeinsDecorateHostWithoutReplacingAnyBlock()
    {
        var blocks = Blocks();
        var veins = new LayerVeinField(17UL, "asteria:overworld", blocks, Layers());
        var chunk = SolidChunk(blocks.GetId("asteria:stone"));
        veins.Apply(chunk, new ChunkCoord(0, 2, 0));
        Assert.True(OreCount(chunk) > 0);
        for (var z = 0; z < Chunk.Size; z++)
        for (var y = 0; y < Chunk.Size; y++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            Assert.Equal(blocks.GetId("asteria:stone"), chunk.GetBlock(x, y, z));
            var surface = chunk.GetSurfaceState(x, y, z);
            if (surface.IsEmpty) continue;
            Assert.Equal(6, surface.Layers.Count);
            Assert.All(surface.Layers, layer => Assert.Equal("asteria:ore_iron", layer.LayerId));
        }
        // Repeated generation never duplicates surface state.
        var before = OreCount(chunk);
        veins.Apply(chunk, new ChunkCoord(0, 2, 0));
        Assert.Equal(before, OreCount(chunk));
    }

    [Fact]
    public void SameCoordinatesGenerateSameOreRegardlessOfOtherChunkOrder()
    {
        var blocks = Blocks();
        var one = new LayerVeinField(94UL, "asteria:overworld", blocks, Layers());
        var two = new LayerVeinField(94UL, "asteria:overworld", blocks, Layers());
        var left = SolidChunk(blocks.GetId("asteria:basalt"));
        var right = SolidChunk(blocks.GetId("asteria:basalt"));
        var other = SolidChunk(blocks.GetId("asteria:basalt"));
        one.Apply(other, new ChunkCoord(1, 2, 0));
        one.Apply(left, new ChunkCoord(0, 2, 0));
        two.Apply(right, new ChunkCoord(0, 2, 0));
        for (var z = 0; z < Chunk.Size; z++)
        for (var y = 0; y < Chunk.Size; y++)
        for (var x = 0; x < Chunk.Size; x++)
            Assert.Equal(left.GetSurfaceState(x, y, z),
                right.GetSurfaceState(x, y, z));
    }

    [Fact]
    public void DifferentDimensionDepthOrHostCannotReceiveVein()
    {
        var blocks = Blocks();
        var emptyField = new LayerVeinField(17, "asteria:umbral", blocks, Layers());
        var stone = SolidChunk(blocks.GetId("asteria:stone"));
        emptyField.Apply(stone, new ChunkCoord(0, 2, 0));
        Assert.Equal(0, OreCount(stone));
        var field = new LayerVeinField(17, "asteria:overworld", blocks, Layers());
        field.Apply(stone, new ChunkCoord(0, 10, 0));
        Assert.Equal(0, OreCount(stone));
        var dirt = SolidChunk(blocks.GetId("asteria:dirt"));
        field.Apply(dirt, new ChunkCoord(0, 2, 0));
        Assert.Equal(0, OreCount(dirt));
    }

    [Fact]
    public void InvalidHostOrMalformedVeinIsRejected()
    {
        var blocks = Blocks();
        var bad = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:bad","texture":"textures/layers/ores/ore_iron.png",
             "scope":"voxel",
             "generation":{"dimension":"asteria:overworld",
                "hostBlocks":["asteria:shell_sphere"],
                "minY":0,"maxY":100,"spacing":12,"radius":2,"chance":0.5}}
            """
        ]);
        Assert.Throws<ArgumentException>(() =>
            new LayerVeinField(0, "asteria:overworld", blocks, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AttachedLayerDefinition.Parse(
                """
                {"id":"asteria:bad","texture":"textures/test.png","scope":"voxel",
                 "generation":{"dimension":"asteria:overworld",
                   "hostBlocks":["asteria:stone"],
                   "minY":20,"maxY":5,"spacing":8,"radius":2,"chance":1}}
                """));
    }

    [Fact]
    public void DefaultPackContainsOnlyMetalVeinsWithRawMaterialDrops()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "layers");
        var parsed = Directory.EnumerateFiles(root, "*.json").Select(File.ReadAllText)
            .Select(AttachedLayerDefinition.Parse).ToArray();
        var generated = parsed.Where(layer => layer.Generation is not null).ToArray();
        Assert.Equal(8, generated.Length);
        foreach (var metal in new[] { "iron", "copper", "lead", "silver",
                                     "tin", "zinc", "nickel", "gold" })
        {
            var ore = Assert.Single(generated, layer => layer.Id == "asteria:ore_" + metal);
            Assert.Equal(LayerRemovalScope.Voxel, ore.Scope);
            Assert.Equal("asteria:overworld", ore.Generation!.Dimension);
            Assert.Equal("asteria:ore_" + metal + "_raw", Assert.Single(ore.Drops).Id);
        }
    }
}
