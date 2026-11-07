using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class EmissiveObjectLightingTests
{
    private const string RedObject = """
        {"id":"asteria:glowing_test","category":"foliage",
         "icon":"textures/objects/grass.png",
         "placementFaces":["top","right"],
         "visual":{"type":"crossedSprite",
                   "texture":"textures/objects/grass.png",
                   "width":0.7,"height":0.6,"planes":2},
         "lightEmission":{"red":12,"green":3,"blue":1}}
        """;

    private static PackContentRegistry<ObjectDefinition> Definitions() =>
        PackContentRegistry<ObjectDefinition>.FromJson(
            [RedObject], ObjectDefinition.Parse, definition => definition.Id);

    [Fact]
    public void EmissionIsValidatedAndDefaultsToDark()
    {
        Assert.Equal(new BlockLightEmission(12, 3, 1),
            ObjectDefinition.Parse(RedObject).LightEmission);
        var dark = ObjectDefinition.Parse(RedObject.Replace(
            "\"lightEmission\":{\"red\":12,\"green\":3,\"blue\":1}", "\"lightEmission\":null"));
        Assert.True(dark.LightEmission.IsDark);
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            RedObject.Replace("\"red\":12", "\"red\":16")));
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            RedObject.Replace("\"green\":3", "\"green\":-1")));
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            RedObject.Replace("\"blue\":1", "\"blue\":1.5")));
    }

    [Fact]
    public void MaterializedChunkSeedsObjectLightBeforeResidencyActivation()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", lightDampening: 15)
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(8, 8, 8, blocks.GetId("asteria:stone"));
        Assert.True(chunk.AddObject(8, 8, 8,
            new ObjectCell("asteria:glowing_test", BlockFace.Top)));

        ChunkLightingSolver.Initialize(chunk, blocks,
            new FluidRegistry([]), Definitions());

        var source = chunk.GetLight(8, 9, 8);
        Assert.Equal((byte)12, source.Red);
        Assert.Equal((byte)3, source.Green);
        Assert.Equal((byte)1, source.Blue);
        Assert.Equal((byte)11, chunk.GetLight(9, 9, 8).Red);
        Assert.Equal((byte)0, chunk.GetLight(8, 8, 8).Red);
    }

    [Fact]
    public void AttachedObjectEmitsFromAdjacentAirAcrossChunkBorder()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", lightDampening: 15)
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var support = new WorldVoxelCoord(Chunk.Size - 1, 8, 8);
        var source = support + (1, 0, 0);
        Assert.True(world.SetBlockAt(support, blocks.GetId("asteria:stone"), out _));
        Assert.True(world.AddObjectAt(support,
            new ObjectCell("asteria:glowing_test", BlockFace.Right)));

        VoxelWorldLightingSolver.Initialize(world, blocks,
            new FluidRegistry([]), Definitions());

        Assert.Equal((byte)12, world.GetLightOrDark(source).Red);
        Assert.Equal((byte)3, world.GetLightOrDark(source).Green);
        Assert.Equal((byte)1, world.GetLightOrDark(source).Blue);
        Assert.Equal((byte)11, world.GetLightOrDark(source + (1, 0, 0)).Red);
        Assert.Equal((byte)0, world.GetLightOrDark(support).Red);
    }

    [Fact]
    public void ObjectPlacementAndRemovalEnqueueTargetLightingAndClearEmission()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", lightDampening: 15)
        ]);
        var fluids = new FluidRegistry([]);
        var objects = Definitions();
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var support = new WorldVoxelCoord(8, 8, 8);
        var target = support + (0, 1, 0);
        Assert.True(world.SetBlockAt(support, blocks.GetId("asteria:stone"), out _));
        VoxelWorldLightingSolver.Initialize(world, blocks, fluids, objects);
        var updates = new WorldUpdateQueue();
        var mutations = new VoxelMutationRuntime(
            world, updates, new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        mutations.ConfigureObjectLighting(objects);

        var cell = new ObjectCell("asteria:glowing_test", BlockFace.Top);
        Assert.True(mutations.AddObjectAt(support, cell));
        Assert.Contains(target, updates.DrainLighting().EditedPositions);
        VoxelWorldLightingSolver.RelightAfterEdits(world, blocks, fluids,
            [target], objects);
        Assert.Equal((byte)12, world.GetLightOrDark(target).Red);

        Assert.True(mutations.RemoveObjectAt(support, cell));
        Assert.Contains(target, updates.DrainLighting().EditedPositions);
        VoxelWorldLightingSolver.RelightAfterEdits(world, blocks, fluids,
            [target], objects);
        Assert.Equal((byte)0, world.GetLightOrDark(target).Red);
        Assert.Equal((byte)0, world.GetLightOrDark(target + (1, 0, 0)).Red);
    }

    [Fact]
    public void DefaultPackAuthoritativeEmissiveVegetationIsObjects()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");
        var blocks = BlockRegistry.FromJson(ReadCategory("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadCategory("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(ReadCategory("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        foreach (var (id, red, green, blue, biome) in new[]
        {
            ("mushroom_glowcap", 3, 7, 10, "asteria:overworld/caverns"),
            ("fern_glow", 1, 7, 5, "asteria:overworld/caverns"),
            ("bloom_aether", 5, 2, 6, "asteria:overworld/floating_islands")
        })
        {
            var key = "asteria:" + id;
            Assert.False(blocks.TryGetId(key, out _));
            var definition = objects.Get(key);
            Assert.Equal(new BlockLightEmission((byte)red, (byte)green, (byte)blue),
                definition.LightEmission);
            Assert.True(Assert.Single(biomes.Get(biome).Decorations,
                rule => rule.Block == key).IsObject);
        }

        IEnumerable<string> ReadCategory(string directory) =>
            Directory.EnumerateFiles(Path.Combine(data, directory), "*.json")
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(File.ReadAllText);
    }
}
