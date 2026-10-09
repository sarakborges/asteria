using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PlayerVisualDefinitionTests
{
    private const string PlayerJson = """
        {
          "id": "asteria:player",
          "model": "models/entities/player/player.glb",
          "skin": "textures/player/256.png",
          "animations": {
            "idle":"Idle", "walk":"Walk", "run":"Run",
            "jump":"Jump", "fall":"Fall"
          }
        }
        """;

    [Fact]
    public void DefinitionParsesImmutableSelectedPackReferences()
    {
        var definition = PlayerVisualDefinition.Parse(PlayerJson);
        Assert.Equal("asteria:player", definition.Id);
        Assert.Equal("models/entities/player/player.glb", definition.Model);
        Assert.Equal("textures/player/256.png", definition.Skin);
        Assert.Equal("Run", definition.Animations["run"]);
        Assert.Equal(5, definition.Animations.Count);
    }

    [Theory]
    [InlineData("../models/player.glb", "textures/player/64.png")]
    [InlineData("res://models/player.glb", "textures/player/64.png")]
    [InlineData("models/player.gltf", "textures/player/64.png")]
    [InlineData("models/player.glb", "textures/player.tres")]
    public void RejectsUnsafeOrUnsupportedAssetReferences(string model, string skin)
    {
        var json = PlayerJson.Replace(
            "models/entities/player/player.glb", model, StringComparison.Ordinal)
            .Replace("textures/player/256.png", skin, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => PlayerVisualDefinition.Parse(json));
    }

    [Fact]
    public void RejectsMissingClip()
    {
        Assert.Throws<FormatException>(() => PlayerVisualDefinition.Parse(
            PlayerJson.Replace("\"jump\":\"Jump\", ", "", StringComparison.Ordinal)));
    }

    [Fact]
    public void AtlasRemapUsesCanonicalFaceRectanglesAnd64PixelCoordinates()
    {
        Assert.True(PlayerSkinUvMapper.TryMap(
            "HeadMesh", new Vector3(-0.5f, 0.5f, 0.5f),
            Vector3.UnitZ, out var front));
        Assert.Equal(new Vector2(8f / 64f, 8f / 64f), front);

        Assert.True(PlayerSkinUvMapper.TryMap(
            "HeadMesh", new Vector3(0.5f, -0.5f, -0.5f),
            -Vector3.UnitZ, out var back));
        Assert.Equal(new Vector2(24f / 64f, 16f / 64f), back);

        Assert.True(PlayerSkinUvMapper.TryMap(
            "BodyMesh", new Vector3(-0.5f, 0.5f, 0.5f),
            Vector3.UnitZ, out var torso));
        Assert.Equal(new Vector2(20f / 64f, 20f / 64f), torso);

        Assert.False(PlayerSkinUvMapper.TryMap(
            "Unknown", Vector3.Zero, Vector3.UnitY, out _));
    }

    [Fact]
    public void DifferentAtlasPartsNeverAliasTheirFrontFaceOrigins()
    {
        var names = new[] {
            "HeadMesh", "HairLayer", "BodyMesh", "RightArmMesh",
            "LeftArmMesh", "RightLegMesh", "LeftLegMesh"
        };
        var positions = names.Select(name =>
        {
            Assert.True(PlayerSkinUvMapper.TryMap(name,
                new Vector3(-0.5f, 0.5f, 0.5f),
                Vector3.UnitZ, out var uv));
            return uv;
        }).ToArray();
        Assert.Equal(names.Length, positions.Distinct().Count());
    }
    [Theory]
    [InlineData("HeadMesh", 40, 8)]
    [InlineData("BodyMesh", 20, 36)]
    [InlineData("RightArmMesh", 44, 36)]
    [InlineData("LeftArmMesh", 52, 52)]
    [InlineData("RightLegMesh", 4, 36)]
    [InlineData("LeftLegMesh", 4, 52)]
    public void OuterLayerFrontUvUsesDedicatedAtlasRectangle(
        string part, int expectedX, int expectedY)
    {
        Assert.True(PlayerSkinUvMapper.TryMapOuter(
            part, new Vector3(-0.5f, 0.5f, 0.5f),
            Vector3.UnitZ, out var uv));
        Assert.Equal(new Vector2(expectedX / 64f, expectedY / 64f), uv);
    }

    [Fact]
    public void HairLayerIsAlreadyAnAuthoredMeshNotAnotherOuterLayer()
    {
        Assert.False(PlayerSkinUvMapper.TryMapOuter(
            "HairLayer", Vector3.Zero, Vector3.UnitZ, out _));
    }

    [Fact]
    public void ActionClipMappingsAreAuthoredNotInferredFromFileNames()
    {
        var json = PlayerJson.Replace(
            "\"fall\":\"Fall\"",
            "\"fall\":\"Fall\", \"hit\":\"Hit\", \"place\":\"Place\", " +
            "\"break\":\"Break\", \"hurt\":\"Hurt\", \"death\":\"Death\"",
            StringComparison.Ordinal);
        var definition = PlayerVisualDefinition.Parse(json);
        Assert.Equal("Hit", definition.Animations["hit"]);
        Assert.Equal("Place", definition.Animations["place"]);
        Assert.Equal("Break", definition.Animations["break"]);
        Assert.Equal("Hurt", definition.Animations["hurt"]);
        Assert.Equal("Death", definition.Animations["death"]);
    }

}
