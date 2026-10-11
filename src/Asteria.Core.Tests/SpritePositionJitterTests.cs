using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SpritePositionJitterTests
{
    [Fact]
    public void JitterIsStableAcrossCallsButVariesByVoxelAndObject()
    {
        var texture = new BlockTextureLayer("textures/objects/grass.png");
        var grass = new BlockDefinition("asteria:grass",
            visual: BlockVisualDefinition.CrossedSprite(
                texture, width: 0.72f,
                positionJitter: new Vector2(0.28f, 0.28f)));
        var flower = new BlockDefinition("asteria:flower", visual: grass.Visual);
        var position = new WorldVoxelCoord(9, 31, -12);

        var expected = SpritePositionJitter.For(grass, position);
        Assert.Equal(expected, SpritePositionJitter.For(grass, position));
        Assert.NotEqual(expected, SpritePositionJitter.For(
            grass, new WorldVoxelCoord(10, 31, -12)));
        Assert.NotEqual(expected, SpritePositionJitter.For(flower, position));
        // Width 0.72 leaves only 0.14 of safe margin per horizontal axis.
        Assert.InRange(expected.X, -0.14f, 0.14f);
        Assert.InRange(expected.Y, -0.14f, 0.14f);
    }

    [Fact]
    public void ZeroJitterOptsOutAndInvalidValuesAreRejected()
    {
        var texture = new BlockTextureLayer("textures/items/pebble.png");
        var block = new BlockDefinition("asteria:pebble",
            visual: BlockVisualDefinition.GroundSprite(
                texture, positionJitter: Vector2.Zero));

        Assert.Equal(Vector2.Zero, SpritePositionJitter.For(
            block, new WorldVoxelCoord(2, 5, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockVisualDefinition.CrossedSprite(texture,
                positionJitter: new Vector2(0.46f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockVisualDefinition.GroundSprite(texture,
                positionJitter: new Vector2(float.NaN, 0f)));
    }

    [Fact]
    public void JsonPositionJitterIsConfigurableAndStrictlyValidated()
    {
        var parsed = BlockDefinitionJson.Parse("""
            {
              "id": "asteria:pebble",
              "visual": {
                "type": "groundSprite",
                "texture": "textures/items/pebble.png",
                "positionJitter": [0.12, 0.18]
              }
            }
            """);
        Assert.Equal(new Vector2(0.12f, 0.18f),
            parsed.Visual.PositionJitter);

        Assert.Throws<FormatException>(() => BlockDefinitionJson.Parse("""
            {"id":"asteria:pebble","visual":{"type":"groundSprite",
              "texture":"textures/items/pebble.png",
              "positionJitter":[0.2]}}
            """));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockDefinitionJson.Parse("""
            {"id":"asteria:pebble","visual":{"type":"groundSprite",
              "texture":"textures/items/pebble.png",
              "positionJitter":[0.5,0]}}
            """));
    }

    [Fact]
    public void GroundSpriteMeshAndRaycastFollowTheSameJitter()
    {
        const string path = "textures/items/pebble.png";
        var block = new BlockDefinition("asteria:pebble",
            visual: BlockVisualDefinition.GroundSprite(
                new BlockTextureLayer(path), width: 0.42f,
                positionJitter: new Vector2(0.2f, 0.2f)),
            isCollidable: false,
            renderMode: BlockRenderMode.Cutout,
            castsShadow: false);
        var blocks = new BlockRegistry([block]);
        var coord = new WorldVoxelCoord(2, 2, 2);
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId(block.Id));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                [path] = 1,
            }), meshletIndex: 0);
        var vertices = Assert.Single(mesh.RenderBatches).Vertices;
        var jitter = SpritePositionJitter.For(block, coord);
        var centerX = coord.X + 0.5f + jitter.X;
        var centerZ = coord.Z + 0.5f + jitter.Y;
        Assert.InRange(vertices.Min(vertex => vertex.Position.X),
            centerX - 0.21001f, centerX - 0.20999f);
        Assert.InRange(vertices.Max(vertex => vertex.Position.X),
            centerX + 0.20999f, centerX + 0.21001f);
        Assert.InRange(vertices.Min(vertex => vertex.Position.Z),
            centerZ - 0.21001f, centerZ - 0.20999f);
        Assert.InRange(vertices.Max(vertex => vertex.Position.Z),
            centerZ + 0.20999f, centerZ + 0.21001f);

        var hit = VoxelWorldRaycaster.Raycast(world, blocks,
            new Vector3(centerX, 3.5f, centerZ), -Vector3.UnitY, 3f);
        Assert.Equal(coord, hit!.Value.Voxel);
        Assert.Equal(0, mesh.CollisionTriangleCount);
    }
}
