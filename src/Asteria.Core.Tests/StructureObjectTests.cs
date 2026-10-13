using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureObjectTests
{
    private const string StructureJson = """
    {
      "id":"asteria:test_object_structure",
      "rotation":true,
      "anchor":{"x":0,"y":0,"z":0},
      "palette":{
        "S":{"block":"asteria:stone"},
        "M":{"object":"asteria:mushroom_red","face":"top"}
      },
      "layers":[
        {"y":0,"rows":["S"]},
        {"y":1,"rows":["M"]}
      ]
    }
    """;

    [Fact]
    public void GeneratedStructuresPublishObjectsOnSurvivingSupportBlocks()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var biome = new BiomeDefinition(
            "asteria:test/flat",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [biome.Id], 32, 18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            generatedSurfaceStructures: [
                new DimensionGeneratedSurfaceStructureDefinition(
                    biome.Id, "asteria:test_object_structure",
                    spacing: 16, chance: 1f, jitter: 0)
            ]);
        var generator = new BiomeWorldGenerator(
            77, dimension, blocks,
            new FluidRegistry([
                new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), .7f)
            ]),
            new BiomeRegistry([biome]),
            new StructureRegistry([StructureDefinitionJson.Parse(StructureJson)]),
            objects: Objects());

        var found = false;
        for (var cx = -1; cx <= 1; cx++)
        for (var cz = -1; cz <= 1; cz++)
        {
            var surfaceY = generator.SurfaceHeight(cx * Chunk.Size + 8,
                cz * Chunk.Size + 8);
            var chunk = generator.Materialize(new ChunkCoord(
                cx, surfaceY / Chunk.Size, cz));
            chunk.VisitObjectCells((x, y, z, obj) => {
                if (obj.Id != "asteria:mushroom_red") return;
                found = true;
                Assert.False(chunk.GetCell(x, y, z).IsEmpty);
                Assert.Equal(BlockFace.Top, obj.Face);
                if (y + 1 < Chunk.Size)
                    Assert.True(chunk.GetCell(x, y + 1, z).IsEmpty);
            });
        }
        Assert.True(found, "Expected at least one generated Structure Object.");
    }

    private static PackContentRegistry<ObjectDefinition> Objects() =>
        PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:mushroom_red","category":"foliage",
             "icon":"textures/objects/mushroom_red.png",
             "placementFaces":["top"],
             "visual":{"type":"crossedSprite",
               "texture":"textures/objects/mushroom_red.png",
               "width":0.72,"height":0.62,"planes":2}}
            """
        ], ObjectDefinition.Parse, obj => obj.Id);

    [Fact]
    public void ParsesStructureObjectsAndValidatesTheirRotatedAttachments()
    {
        var structure = StructureDefinitionJson.Parse(StructureJson);
        var obj = Assert.Single(structure.Objects);
        Assert.Equal("asteria:mushroom_red", obj.Object);
        Assert.Equal(BlockFace.Top, obj.Face);
        Assert.Equal(1, obj.Y);
        var registry = new StructureRegistry([structure]);
        registry.ValidateBlocks(new BlockRegistry([new BlockDefinition("asteria:stone")]));
        registry.ValidateObjects(Objects());
    }

    [Fact]
    public void RejectsConflictingOrUnknownStructureObjectPayload()
    {
        Assert.Throws<FormatException>(() => StructureDefinitionJson.Parse(
            StructureJson.Replace(
                "\"object\":\"asteria:mushroom_red\"",
                "\"object\":\"asteria:mushroom_red\",\"block\":\"asteria:stone\"")));
        var registry = new StructureRegistry([
            StructureDefinitionJson.Parse(StructureJson.Replace(
                "asteria:mushroom_red", "asteria:missing"))]);
        Assert.Throws<ArgumentException>(() => registry.ValidateObjects(Objects()));
    }

    [Fact]
    public void CanonicalStructureBatchCommitsObjectsOnSupportingBlocks()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        var support = new WorldVoxelCoord(2, 1, 2);
        var target = new WorldVoxelCoord(2, 2, 2);
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var objectCell = new ObjectCell("asteria:mushroom_red", BlockFace.Top);
        var supportBlock = new VoxelCell(blocks.GetId("asteria:stone"));

        // Same batch can construct its support and attach the Object.
        Assert.True(mutations.ApplyStructureChanges([
            new VoxelStructureChange(support,
                new BlockStateSnapshot(supportBlock, MicroblockMask.Empty,
                    BlockSurfaceState.Empty), FluidCell.Empty)
        ], [
            new VoxelStructureObjectChange(target, support, objectCell)
        ]));
        Assert.Equal(objectCell, Assert.Single(world.TryGetObjects(support,
            out var objects) ? objects : []));
        Assert.True(world.GetCellOrEmpty(target).IsEmpty);
    }

    [Fact]
    public void InvalidTargetRejectsEntireStructureBatchWithoutPartialVoxelEdits()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        var support = new WorldVoxelCoord(2, 1, 2);
        var target = new WorldVoxelCoord(2, 2, 2);
        Assert.True(world.SetBlockAt(target, blocks.GetId("asteria:stone"), out _));
        var before = world.Revision;
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());

        Assert.False(mutations.ApplyStructureChanges([
            new VoxelStructureChange(support,
                new BlockStateSnapshot(
                    new VoxelCell(blocks.GetId("asteria:stone")),
                    MicroblockMask.Empty, BlockSurfaceState.Empty), FluidCell.Empty)
        ], [
            new VoxelStructureObjectChange(target, support,
                new ObjectCell("asteria:mushroom_red", BlockFace.Top))
        ]));
        Assert.Equal(before, world.Revision);
        Assert.True(world.GetCellOrEmpty(support).IsEmpty);
    }
}
