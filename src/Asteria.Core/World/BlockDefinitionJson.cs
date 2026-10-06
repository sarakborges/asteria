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
        var tint = ParseTint(OptionalString(root, "tint"));
        var textures = ParseTextures(root);
        var textureRotations = ParseTextureRotations(root);
        var mining = ParseMining(root);
        var shape = ParseShape(root);
        var orientations = ParseOrientations(root);
        var variant = ParseVariant(root);
        var lightDampening = OptionalByte(root, "lightDampening") ?? (byte)15;
        var castsShadow = OptionalBoolean(root, "castsShadow") ?? true;
        var isCollidable = OptionalBoolean(root, "isCollidable") ?? true;
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
            rotateTexture: textureRotations,
            mining: mining,
            shape: shape,
            orientations: orientations,
            variant: variant,
            isCollidable: isCollidable,
            renderMode: renderMode,
            castsShadow: castsShadow,
            lightDampening: lightDampening,
            lightEmission: emission,
            previewColor: previewColor);
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
            StringArray(mining, "preferredTools"));
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
