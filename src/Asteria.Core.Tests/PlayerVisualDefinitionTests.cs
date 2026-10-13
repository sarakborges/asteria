using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PlayerVisualDefinitionTests
{
    private const string PlayerJson = """
        {
          "id": "asteria:player",
          "model": "models/entities/player/player.glb",
          "skin": "textures/player/64.png",
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
        Assert.Equal("textures/player/64.png", definition.Skin);
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
            .Replace("textures/player/64.png", skin, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => PlayerVisualDefinition.Parse(json));
    }

    [Fact]
    public void MissingSkinFieldUsesDefaultAtlasDirectory()
    {
        var json = PlayerJson.Replace(
            "\"skin\": \"textures/player/64.png\",", "",
            StringComparison.Ordinal);
        Assert.Equal("textures/player/64.png",
            PlayerVisualDefinition.Parse(json).Skin);
    }

    [Fact]
    public void SkinFallbackPicksTheHighestAvailableResolution()
    {
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            "textures/player/64.png",
            "textures/player/256.png",
        };
        var candidates = PlayerSkinAtlasCandidates.Paths("textures/player/64.png");
        Assert.Equal(new[] {
            "textures/player/512.png",
            "textures/player/256.png",
            "textures/player/128.png",
            "textures/player/64.png",
        }, candidates);
        Assert.Equal("textures/player/256.png",
            candidates.FirstOrDefault(available.Contains));
    }

    [Theory]
    [InlineData("textures/player/512.png")]
    [InlineData("textures/player/256.png")]
    [InlineData("textures/player/128.png")]
    [InlineData("textures/player/64.png")]
    public void MissingPreferredResolutionFallsBackTo64(string preferred)
    {
        var candidates = PlayerSkinAtlasCandidates.Paths(preferred);
        Assert.Equal("textures/player/64.png",
            candidates.FirstOrDefault(path =>
                path == "textures/player/64.png"));
    }

    [Fact]
    public void CustomNamedSkinIsNotReplacedByUnrelatedAtlas()
    {
        Assert.Equal(new[] { "textures/characters/custom.png" },
            PlayerSkinAtlasCandidates.Paths("textures/characters/custom.png"));
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

    [Theory]
    [InlineData("HeadMesh")]
    [InlineData("BodyMesh")]
    [InlineData("RightArmMesh")]
    [InlineData("LeftArmMesh")]
    [InlineData("RightLegMesh")]
    [InlineData("LeftLegMesh")]
    public void OuterSkinUvIsAnOffsetFromAuthoredGlbUvForEveryFace(string part)
    {
        Assert.True(PlayerSkinUvMapper.TryOuterUvOffset(part, out var offset));
        var samples = new (Vector3 Position, Vector3 Normal)[]
        {
            (new(-0.5f, 0.5f, 0.5f), Vector3.UnitZ),
            (new(0.5f, -0.5f, -0.5f), -Vector3.UnitZ),
            (new(-0.5f, 0.5f, -0.5f), -Vector3.UnitX),
            (new(0.5f, -0.5f, 0.5f), Vector3.UnitX),
            (new(-0.5f, 0.5f, -0.5f), Vector3.UnitY),
            (new(0.5f, -0.5f, 0.5f), -Vector3.UnitY),
        };
        foreach (var (position, normal) in samples)
        {
            Assert.True(PlayerSkinUvMapper.TryMap(part, position, normal, out var inner));
            Assert.True(PlayerSkinUvMapper.TryMapOuter(part, position, normal, out var outer));
            Assert.Equal(outer, inner + offset);
        }
    }

    [Fact]
    public void HairLayerIsAlreadyAnAuthoredMeshNotAnotherOuterLayer()
    {
        Assert.False(PlayerSkinUvMapper.TryMapOuter(
            "HairLayer", Vector3.Zero, Vector3.UnitZ, out _));
        Assert.False(PlayerSkinUvMapper.TryOuterUvOffset("HairLayer", out _));
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

    [Theory]
    [InlineData("HeadMesh", 0, 0, 8, 8, 8)]
    [InlineData("HairLayer", 32, 0, 8, 8, 8)]
    [InlineData("BodyMesh", 16, 16, 8, 12, 4)]
    [InlineData("RightArmMesh", 40, 16, 4, 12, 4)]
    [InlineData("LeftArmMesh", 32, 48, 4, 12, 4)]
    [InlineData("RightLegMesh", 0, 16, 4, 12, 4)]
    [InlineData("LeftLegMesh", 16, 48, 4, 12, 4)]
    public void AllSixFacesUseCanonicalMineCloneSkinRectangles(
        string part, int x, int y, int width, int height, int depth)
    {
        var faces = new (Vector3 Corner, Vector3 Normal,
            int ExpectedX, int ExpectedY)[]
        {
            (new(-.5f, .5f, -.5f), -Vector3.UnitX,
                x, y + depth),
            (new(-.5f, .5f, .5f), Vector3.UnitZ,
                x + depth, y + depth),
            (new(.5f, .5f, .5f), Vector3.UnitX,
                x + depth + width, y + depth),
            (new(.5f, .5f, -.5f), -Vector3.UnitZ,
                x + depth * 2 + width, y + depth),
            (new(-.5f, .5f, -.5f), Vector3.UnitY,
                x + depth, y),
            (new(-.5f, -.5f, .5f), -Vector3.UnitY,
                x + depth + width, y)
        };

        foreach (var (corner, normal, px, py) in faces)
        {
            Assert.True(PlayerSkinUvMapper.TryMap(part, corner, normal, out var uv));
            Assert.Equal(new Vector2(px / 64f, py / 64f), uv);
        }

        // Geometry is normalized before applying any texture resolution.
        Assert.True(PlayerSkinUvMapper.TryMap(
            part, new Vector3(.5f, -.5f, .5f), Vector3.UnitZ,
            out var bottomRight));
        Assert.Equal((x + depth + width) / 64f, bottomRight.X);
        Assert.Equal((y + depth + height) / 64f, bottomRight.Y);
    }

    [Theory]
    [InlineData("HeadMesh", 32, 0, 8, 8, 8)]
    [InlineData("BodyMesh", 16, 32, 8, 12, 4)]
    [InlineData("RightArmMesh", 40, 32, 4, 12, 4)]
    [InlineData("LeftArmMesh", 48, 48, 4, 12, 4)]
    [InlineData("RightLegMesh", 0, 32, 4, 12, 4)]
    [InlineData("LeftLegMesh", 0, 48, 4, 12, 4)]
    public void EveryOuterLayerMapsAllSixFacesIndependently(
        string part, int x, int y, int width, int height, int depth)
    {
        var normals = new[]
        {
            -Vector3.UnitX, Vector3.UnitZ, Vector3.UnitX,
            -Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitY
        };
        var corners = new[]
        {
            new Vector3(-.5f, .5f, -.5f),
            new Vector3(-.5f, .5f, .5f),
            new Vector3(.5f, .5f, .5f),
            new Vector3(.5f, .5f, -.5f),
            new Vector3(-.5f, .5f, -.5f),
            new Vector3(-.5f, -.5f, .5f)
        };
        var atlasCorners = new[]
        {
            (x, y + depth),
            (x + depth, y + depth),
            (x + depth + width, y + depth),
            (x + depth * 2 + width, y + depth),
            (x + depth, y),
            (x + depth + width, y)
        };
        for (var face = 0; face < normals.Length; face++)
        {
            Assert.True(PlayerSkinUvMapper.TryMapOuter(
                part, corners[face], normals[face], out var uv));
            var expected = atlasCorners[face];
            Assert.Equal(new Vector2(expected.Item1 / 64f,
                expected.Item2 / 64f), uv);
            Assert.True(PlayerSkinUvMapper.TryMap(
                part, corners[face], normals[face], out var baseUv));
            Assert.NotEqual(baseUv, uv);
        }
    }

    [Fact]
    public void SkinAtlasMappingRejectsUnknownFacesInsteadOfSilentlyWrapping()
    {
        Assert.False(PlayerSkinUvMapper.TryMap("HeadMesh",
            Vector3.Zero, Vector3.Zero, out _));
        Assert.False(PlayerSkinUvMapper.TryMapOuter("HairLayer",
            Vector3.Zero, Vector3.UnitZ, out _));
        Assert.False(PlayerSkinUvMapper.TryOuterUvOffset("Unsupported", out _));
    }

}
