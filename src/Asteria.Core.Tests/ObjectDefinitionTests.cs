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

    private const string Extruded = """
    {"id":"asteria:seashell","category":"foliage",
     "icon":"textures/objects/seashell.png",
     "visual":{"type":"extrudedSprite","texture":"textures/objects/seashell.png",
       "height":0.012,"size":[0.59,0.42],"baseOffset":0.0125}}
    """;

    [Fact]
    public void ExtrudedSpriteReadsIndependentWidthDepthThicknessAndTarget()
    {
        var obj = ObjectDefinition.Parse(Extruded);
        Assert.Equal(ObjectVisualKind.ExtrudedSprite, obj.Visual.Kind);
        Assert.Equal(.59f, obj.Visual.Width);
        Assert.Equal(.42f, obj.Visual.Depth);
        Assert.Equal(.012f, obj.Visual.Height);
        Assert.Equal(.0125f, obj.Visual.BaseOffset);
        Assert.Equal(new Vector3(.59f, .14f, .42f), obj.Target.Size);
        Assert.Equal(new Vector3(0f, .07f, 0f), obj.Target.CenterOffset);
    }

    [Fact]
    public void ExtrudedSpriteRejectsInvalidSizeAndThickness()
    {
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Extruded.Replace("[0.59,0.42]", "[0.59,-0.42]")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Extruded.Replace("[0.59,0.42]", "[0,0.42]")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Extruded.Replace("\"height\":0.012", "\"height\":0")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(Extruded.Replace("\"baseOffset\":0.0125", "\"baseOffset\":3")));
    }

    private const string Model = """
    {"id":"asteria:small_sculpture","category":"foliage",
     "icon":"textures/objects/sculpture.png",
     "placementFaces":["top","front"],
     "visual":{"type":"model","path":"models/objects/sculpture.glb"},
     "target":{"size":[1.1,1.8,0.7],"centerOffset":[0.1,0.9,0]}}
    """;

    [Fact]
    public void ModelParsesPortablePathAndRequiresAuthoredHitbox()
    {
        var obj = ObjectDefinition.Parse(Model);
        Assert.Equal(ObjectVisualKind.Model, obj.Visual.Kind);
        Assert.Equal("models/objects/sculpture.glb", obj.Visual.ModelPath);
        Assert.Equal("", obj.Visual.Texture);
        Assert.Null(obj.Visual.Parts);
        Assert.Equal(new Vector3(1.1f, 1.8f, .7f), obj.Target.Size);
        Assert.Equal(new Vector3(.1f, .9f, 0), obj.Target.CenterOffset);
        Assert.True(obj.Supports(BlockFace.Front));
        Assert.False(PlayerHeldVisualLayout.ObjectPose(obj).FaceCamera);
    }

    [Theory]
    [InlineData("models/objects/../sculpture.glb")]
    [InlineData("/models/objects/sculpture.glb")]
    [InlineData("C:/sculpture.glb")]
    [InlineData("models/objects/sculpture.gltf")]
    [InlineData("models\\objects\\sculpture.glb")]
    public void ModelRejectsUnsafeOrUnsupportedPaths(string path)
    {
        var json = Model.Replace("models/objects/sculpture.glb",
            path.Replace("\\", "\\\\"));
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(json));
    }

    [Fact]
    public void ModelRejectsMissingTargetAndSpriteGeometry()
    {
        var withoutTarget = Model[..Model.IndexOf(",\n     \"target\"",
            StringComparison.Ordinal)] + "}";
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(withoutTarget));
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            Model.Replace(
                "\"path\":\"models/objects/sculpture.glb\"",
                "\"texture\":\"textures/objects/sculpture.png\"")));
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(
            Model.Replace("\"type\":\"model\"",
                "\"type\":\"model\",\"parts\":[]")));
    }
}
