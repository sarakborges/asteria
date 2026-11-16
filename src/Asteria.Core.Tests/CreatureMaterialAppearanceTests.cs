using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class CreatureMaterialAppearanceTests
{
    private const string Alpha = """
        {
          "id":"asteria:slime_alpha",
          "model":"models/creatures/shared.glb",
          "health":10,
          "maxPerType":8,
          "collider":{"size":[1,1,1],"centerOffset":[0,0.5,0]},
          "jumpSpeed":4,
          "moveSpeed":1,
          "jumpInterval":2,
          "anticipationSeconds":0.2,
          "landingSeconds":0.2,
          "animations":{"idle":"Idle"},
          "textures":{
            "SlimeFace":"textures/creatures/alpha.png",
            "BodyColor":"textures/creatures/body.png"
          },
          "unlitMaterials":["SlimeFace","ElementAccent","ElementPale"]
        }
        """;

    [Fact]
    public void FaceCanBeBothTexturedAndUnlit()
    {
        var creature = CreatureDefinition.Parse(Alpha);
        var face = CreatureMaterialAppearance.For(creature, "SlimeFace");

        Assert.Equal("textures/creatures/alpha.png", face.TexturePath);
        Assert.True(face.Unlit);
        Assert.True(face.HasOverrides);
    }

    [Theory]
    [InlineData("ElementAccent")]
    [InlineData("ElementPale")]
    public void UnlitOnlyMaterialRetainsOriginalGltfTexture(string material)
    {
        var creature = CreatureDefinition.Parse(Alpha);
        var appearance = CreatureMaterialAppearance.For(creature, material);

        Assert.Null(appearance.TexturePath);
        Assert.True(appearance.Unlit);
        Assert.True(appearance.HasOverrides);
    }

    [Fact]
    public void TextureOnlyOverrideRetainsLighting()
    {
        var creature = CreatureDefinition.Parse(Alpha);
        var appearance = CreatureMaterialAppearance.For(creature, "BodyColor");

        Assert.Equal("textures/creatures/body.png", appearance.TexturePath);
        Assert.False(appearance.Unlit);
        Assert.True(appearance.HasOverrides);
    }

    [Fact]
    public void UnmentionedMaterialKeepsItsImportedMaterial()
    {
        var creature = CreatureDefinition.Parse(Alpha);
        var appearance = CreatureMaterialAppearance.For(creature, "DecorativeFlower");

        Assert.Null(appearance.TexturePath);
        Assert.False(appearance.Unlit);
        Assert.False(appearance.HasOverrides);
    }

    [Fact]
    public void DistinctSpeciesCanShareGeometryButNotAppearance()
    {
        var alpha = CreatureDefinition.Parse(Alpha);
        var beta = CreatureDefinition.Parse(Alpha
            .Replace("slime_alpha", "slime_beta", StringComparison.Ordinal)
            .Replace("alpha.png", "beta.png", StringComparison.Ordinal)
            .Replace("\"ElementAccent\",", "", StringComparison.Ordinal));

        Assert.Equal(alpha.Model, beta.Model);
        Assert.NotEqual(alpha.Id, beta.Id);
        Assert.NotEqual(
            CreatureMaterialAppearance.For(alpha, "SlimeFace").TexturePath,
            CreatureMaterialAppearance.For(beta, "SlimeFace").TexturePath);
        Assert.True(CreatureMaterialAppearance.For(alpha, "ElementAccent").Unlit);
        Assert.False(CreatureMaterialAppearance.For(beta, "ElementAccent").Unlit);
    }

    [Fact]
    public void InvalidMaterialNameOrDefinitionIsRejected()
    {
        var alpha = CreatureDefinition.Parse(Alpha);
        Assert.Throws<ArgumentNullException>(
            () => CreatureMaterialAppearance.For(null!, "SlimeFace"));
        Assert.Throws<ArgumentNullException>(
            () => CreatureMaterialAppearance.For(alpha, null!));
    }
}
