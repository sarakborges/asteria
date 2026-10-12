using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectDefinitionTests
{
    private const string Grass = """
    {
      "id":"asteria:grass", "category":"foliage",
      "icon":"textures/objects/grass.png",
      "placementFaces":["top","front"],
      "visual":{"type":"crossedSprite",
        "texture":"textures/objects/grass.png",
        "width":0.72,"height":0.62,"planes":4,
        "alphaCutoff":0.5},
      "target":{"size":[0.72,0.62,0.72],"centerOffset":[0,0.31,0]},
      "positionJitter":[0.28,0.28],
      "dropSelf":false
    }
    """;

    [Fact]
    public void ParsesPortableObjectGeometryTargetAndPlacementPolicy()
    {
        var obj = ObjectDefinition.Parse(Grass);
        Assert.Equal("asteria:grass", obj.Id);
        Assert.Equal(ObjectVisualKind.CrossedSprite, obj.Visual.Kind);
        Assert.Equal(4, obj.Visual.Planes);
        Assert.Equal(.72f, obj.Visual.Width);
        Assert.Equal(new Vector3(.72f, .62f, .72f), obj.Target.Size);
        Assert.Equal(new Vector3(0, .31f, 0), obj.Target.CenterOffset);
        Assert.Equal(new Vector2(.28f), obj.PositionJitter);
        Assert.False(obj.DropSelf);
        Assert.True(obj.Supports(BlockFace.Front));
        Assert.False(obj.Supports(BlockFace.Left));
    }

    [Fact]
    public void RejectsMalformedUnboundedOrUnsafeContent()
    {
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"planes\":4", "\"planes\":9")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"planes\":4", "\"planes\":1")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"positionJitter\":[0.28,0.28]",
                "\"positionJitter\":[0.9,0.0]")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"front\"", "\"top\"")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"textures/objects/grass.png\"",
                "\"../unsafe.png\"")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"height\":0.62",
                "\"height\":-0.1")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Grass.Replace("\"size\":[0.72,0.62,0.72]",
                "\"size\":[0,0.62,0.72]")));
    }

    [Fact]
    public void RegistryRejectsDuplicatesAndOrdersById()
    {
        var second = Grass.Replace("asteria:grass", "asteria:zgrass");
        var registry = PackContentRegistry<ObjectDefinition>.FromJson(
            [second, Grass], ObjectDefinition.Parse, value => value.Id);
        Assert.Equal(["asteria:grass", "asteria:zgrass"],
            registry.Definitions.Select(value => value.Id));
        Assert.Throws<ArgumentException>(() =>
            PackContentRegistry<ObjectDefinition>.FromJson(
                [Grass, Grass], ObjectDefinition.Parse, value => value.Id));
    }

    [Fact]
    public void ExtrudedSpriteDefinitionHasSeparateTarget()
    {
        var obj = ObjectDefinition.Parse("""
        {"id":"asteria:pebble","category":"foliage",
         "icon":"textures/objects/pebble.png",
         "visual":{"type":"extrudedSprite","texture":"textures/objects/pebble.png",
           "height":0.012,"size":[0.42,0.42],"baseOffset":0.0125},
         "target":{"size":[0.42,0.14,0.42],"centerOffset":[0,0.07,0]}}
        """);
        Assert.Equal(ObjectVisualKind.ExtrudedSprite, obj.Visual.Kind);
        Assert.Equal(BlockFace.Top, Assert.Single(obj.PlacementFaces));
        Assert.Equal(.14f, obj.Target.Size.Y);
    }
}
