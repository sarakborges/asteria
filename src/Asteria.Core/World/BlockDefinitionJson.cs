using System.Text.Json;

namespace Asteria.Core.World;

public static class BlockDefinitionJson
{
    public static BlockDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var id = RequiredString(root, "id");
        var category = OptionalString(root, "category") ?? "uncategorized";
        var tags = StringArray(root, "tags");
        var secondaryProperties = StringArray(root, "secondaryProperties");
        var tint = ParseTint(OptionalString(root, "tint"));
        var textures = ParseTextures(root);
        var visual = ParseVisual(root);
        var textureRotations = ParseTextureRotations(root);
        var mining = ParseMining(root);
        var shape = ParseShape(root);
        var orientations = ParseOrientations(root);
        var placementFaces = ParsePlacementFaces(root);
        var variant = ParseVariant(root);
        var lightDampening = OptionalByte(root, "lightDampening") ?? (byte)15;
        var castsShadow = OptionalBoolean(root, "castsShadow") ?? true;
        var isCollidable = OptionalBoolean(root, "isCollidable") ?? true;
        var dropsSelf = OptionalBoolean(root, "dropsSelf") ?? true;
        var interaction = OptionalString(root, "interaction") switch
        {
            null or "break" => BlockInteractionKind.Break,
            "pickup" => BlockInteractionKind.Pickup,
            var value => throw new FormatException($"Unsupported block interaction: {value}"),
        };
        var pickupItemId = OptionalString(root, "pickupItem");
        var alphaBlend = OptionalBoolean(root, "alphaBlend") ?? false;
        var hasAlphaCutoff = root.TryGetProperty("alphaCutoff", out var alphaCutoff) &&
                             alphaCutoff.ValueKind != JsonValueKind.Null;
        var renderMode = alphaBlend
            ? BlockRenderMode.Translucent
            : hasAlphaCutoff
                ? BlockRenderMode.Cutout
                : BlockRenderMode.Opaque;

        var emission = ParseLightEmission(root, id);

        var previewColor = root.TryGetProperty("previewColor", out var previewElement)
            ? BlockPreviewColor.Parse(previewElement.GetString() ?? throw new FormatException($"Block {id} previewColor must be a string."))
            : BlockPreviewColor.Missing;

        return new BlockDefinition(
            id: id,
            category: category,
            tags: tags,
            tint: tint,
            textures: textures,
            visual: visual,
            rotateTexture: textureRotations,
            mining: mining,
            shape: shape,
            orientations: orientations,
            placementFaces: placementFaces,
            variant: variant,
            isCollidable: isCollidable,
            renderMode: renderMode,
            castsShadow: castsShadow,
            lightDampening: lightDampening,
            lightEmission: emission,
            previewColor: previewColor,
            dropsSelf: dropsSelf,
            interaction: interaction,
            pickupItemId: pickupItemId,
            secondaryProperties: secondaryProperties,
            windSway: OptionalBoolean(root, "windSway") ?? false);
    }

    private static BlockLightEmission ParseLightEmission(JsonElement root, string blockId)
    {
        if (!root.TryGetProperty("lightEmission", out var emission) ||
            emission.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        if (emission.ValueKind == JsonValueKind.Number)
        {
            if (!emission.TryGetByte(out var level) || level > VoxelLight.MaxLevel)
            {
                throw new FormatException($"Block {blockId} lightEmission must be within 0..15.");
            }

            return new BlockLightEmission(level, level, level);
        }

        if (emission.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException(
                $"Block {blockId} lightEmission must be a 0..15 number or RGB object.");
        }

        var red = RequiredLightChannel(emission, "red", blockId);
        var green = RequiredLightChannel(emission, "green", blockId);
        var blue = RequiredLightChannel(emission, "blue", blockId);
        return new BlockLightEmission(red, green, blue);
    }

    private static byte RequiredLightChannel(
        JsonElement emission,
        string channel,
        string blockId)
    {
        if (!emission.TryGetProperty(channel, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetByte(out var level) ||
            level > VoxelLight.MaxLevel)
        {
            throw new FormatException(
                $"Block {blockId} lightEmission.{channel} must be within 0..15.");
        }

        return level;
    }

    private static BlockShapeDefinition ParseShape(JsonElement root)
    {
        if (!root.TryGetProperty("shape", out var shape))
        {
            return BlockShapeDefinition.Cube;
        }

        if (shape.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("shape must be an object.");
        }

        return RequiredString(shape, "type") switch
        {
            "cube" => BlockShapeDefinition.Cube,
            "layer" => ParseLayerShape(shape),
            "hollow" => BlockShapeDefinition.Hollow(
                OptionalSingle(shape, "wallThickness") ?? (1f / 16f)),
            var type => throw new FormatException($"Unknown block shape type: {type}"),
        };
    }

    private static BlockShapeDefinition ParseLayerShape(JsonElement shape)
    {
        var thickness = RequiredSingle(shape, "thickness");
        return OptionalString(shape, "placement") switch
        {
            null or "surface" => BlockShapeDefinition.SurfaceLayer(
                thickness,
                RequiredString(shape, "stackTo")),
            "center" => BlockShapeDefinition.CenteredLayer(thickness),
            var placement => throw new FormatException($"Unknown layer placement: {placement}"),
        };
    }

    private static IReadOnlyList<BlockOrientation> ParseOrientations(JsonElement root)
    {
        if (!root.TryGetProperty("orientations", out var orientations))
        {
            return [BlockOrientation.Y];
        }

        if (orientations.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("orientations must be an array.");
        }

        return orientations
            .EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? ParseOrientation(value.GetString())
                : throw new FormatException("orientations can contain only strings."))
            .ToArray();
    }

    private static BlockOrientation ParseOrientation(string? value) => value switch
    {
        "x" => BlockOrientation.X,
        "y" => BlockOrientation.Y,
        "z" => BlockOrientation.Z,
        _ => throw new FormatException($"Unknown block orientation: {value}"),
    };

    private static BlockVariantDefinition? ParseVariant(JsonElement root)
    {
        if (!root.TryGetProperty("variant", out var variant))
        {
            return null;
        }

        if (variant.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("variant must be an object.");
        }

        return new BlockVariantDefinition(
            RequiredString(variant, "family"),
            RequiredString(variant, "key"));
    }

    private static BlockTextureSet ParseTextures(JsonElement root)
    {
        if (!root.TryGetProperty("textures", out var textures))
        {
            return new BlockTextureSet();
        }

        if (textures.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Block textures must be an object.");
        }

        return new BlockTextureSet(
            FaceLayers(textures, "top"),
            FaceLayers(textures, "bottom"),
            FaceLayers(textures, "left"),
            FaceLayers(textures, "right"),
            FaceLayers(textures, "front"),
            FaceLayers(textures, "back"));
    }

    private static IReadOnlyList<BlockTextureLayer> FaceLayers(JsonElement textures, string propertyName)
    {
        if (!textures.TryGetProperty(propertyName, out var value))
        {
            return Array.Empty<BlockTextureLayer>();
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => [new BlockTextureLayer(value.GetString()!)],
            JsonValueKind.Object => [ParseTextureLayer(value)],
            JsonValueKind.Array => value.EnumerateArray().Select(ParseTextureLayerValue).ToArray(),
            _ => throw new FormatException($"Texture face {propertyName} must be a string, object, or array."),
        };
    }

    private static BlockTextureLayer ParseTextureLayerValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new BlockTextureLayer(value.GetString()!),
        JsonValueKind.Object => ParseTextureLayer(value),
        _ => throw new FormatException("Texture layer arrays can contain only strings or objects."),
    };

    private static BlockTextureLayer ParseTextureLayer(JsonElement value)
    {
        var texture = RequiredString(value, "texture");
        var dyable =
            OptionalBoolean(value, "dyable") ??
            OptionalBoolean(value, "dyeable") ??
            false;
        return new BlockTextureLayer(texture, dyable);
    }

    private static BlockVisualDefinition ParseVisual(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "visual",
                out var visual))
        {
            return BlockVisualDefinition.Geometry;
        }

        if (visual.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "visual must be an object.");
        }

        return RequiredString(
                visual,
                "type") switch
        {
            "geometry" =>
                BlockVisualDefinition.Geometry,
            "groundSprite" =>
                BlockVisualDefinition.GroundSprite(
                    ParseTextureLayerValue(
                        RequiredProperty(visual, "texture")),
                    OptionalSingle(visual, "width") ?? 0.42f,
                    OptionalSingle(visual, "height") ?? 0.012f,
                    OptionalSingle(visual, "baseOffset") ?? 0.0125f,
                    OptionalSingle(visual, "targetHeight") ?? 0.14f),
            "crossedSprite" =>
                BlockVisualDefinition.CrossedSprite(
                    ParseTextureLayerValue(
                        RequiredProperty(
                            visual,
                            "texture")),
                    OptionalSingle(
                        visual,
                        "width") ??
                    0.72f,
                    OptionalSingle(
                        visual,
                        "height") ??
                    0.62f,
                    OptionalInt32(
                        visual,
                        "planes") ??
                    2,
                    OptionalSingle(
                        visual,
                        "baseOffset") ??
                    0f),
            var type =>
                throw new FormatException(
                    $"Unknown block visual type: {type}"),
        };
    }

    private static IReadOnlyList<BlockFace>
        ParsePlacementFaces(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "placementFaces",
                out var placementFaces))
        {
            return Enum.GetValues<BlockFace>();
        }

        if (placementFaces.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                "placementFaces must be an array.");
        }

        return placementFaces
            .EnumerateArray()
            .Select(value =>
                value.ValueKind ==
                    JsonValueKind.String
                    ? ParseBlockFace(
                        value.GetString())
                    : throw new FormatException(
                        "placementFaces can contain only strings."))
            .ToArray();
    }

    private static BlockFace ParseBlockFace(
        string? value) =>
        value switch
        {
            "top" => BlockFace.Top,
            "bottom" => BlockFace.Bottom,
            "left" => BlockFace.Left,
            "right" => BlockFace.Right,
            "front" => BlockFace.Front,
            "back" => BlockFace.Back,
            _ => throw new FormatException(
                $"Unknown placement face: {value}"),
        };

    private static JsonElement RequiredProperty(
        JsonElement value,
        string propertyName)
    {
        if (!value.TryGetProperty(
                propertyName,
                out var property))
        {
            throw new FormatException(
                $"Missing required property: {propertyName}");
        }

        return property;
    }

    private static int? OptionalInt32(
        JsonElement value,
        string propertyName)
    {
        if (!value.TryGetProperty(
                propertyName,
                out var property) ||
            property.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind !=
                JsonValueKind.Number ||
            !property.TryGetInt32(
                out var result))
        {
            throw new FormatException(
                $"{propertyName} must be an integer.");
        }

        return result;
    }

    private static BlockTextureRotations ParseTextureRotations(JsonElement root)
    {
        if (!root.TryGetProperty("rotateTexture", out var rotations))
        {
            return default;
        }

        if (rotations.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("rotateTexture must be an object.");
        }

        return new BlockTextureRotations(
            OptionalBoolean(rotations, "top") ?? false,
            OptionalBoolean(rotations, "bottom") ?? false,
            OptionalBoolean(rotations, "left") ?? false,
            OptionalBoolean(rotations, "right") ?? false,
            OptionalBoolean(rotations, "front") ?? false,
            OptionalBoolean(rotations, "back") ?? false);
    }

    private static BlockMiningDefinition ParseMining(JsonElement root)
    {
        if (!root.TryGetProperty("mining", out var mining))
        {
            return new BlockMiningDefinition();
        }

        if (mining.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("mining must be an object.");
        }

        return new BlockMiningDefinition(
            OptionalSingle(mining, "hardness") ?? 1f,
            StringArray(mining, "requiredTools"),
            StringArray(mining, "preferredTools"),
            OptionalBoolean(mining, "unbreakable") ?? false);
    }

    private static BlockTint ParseTint(string? value) => value switch
    {
        null or "none" => BlockTint.None,
        "grass" => BlockTint.Grass,
        "leaf" => BlockTint.Leaf,
        "foliage" => BlockTint.Foliage,
        _ => throw new FormatException($"Unknown block tint: {value}"),
    };

    private static string RequiredString(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Missing required string property: {propertyName}");
        }

        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"{propertyName} must be a string.");
        }

        return property.GetString();
    }

    private static bool? OptionalBoolean(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FormatException($"{propertyName} must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static byte? OptionalByte(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetByte(out var result))
        {
            throw new FormatException($"{propertyName} must be an unsigned byte.");
        }

        return result;
    }

    private static float RequiredSingle(JsonElement value, string propertyName) =>
        OptionalSingle(value, propertyName) ??
        throw new FormatException($"Missing required number property: {propertyName}");

    private static float? OptionalSingle(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetSingle(out var result))
        {
            throw new FormatException($"{propertyName} must be a number.");
        }

        return result;
    }

    private static IReadOnlyList<string> StringArray(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return Array.Empty<string>();
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"{propertyName} must be an array.");
        }

        return property
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new FormatException($"{propertyName} can contain only strings."))
            .ToArray();
    }
}
