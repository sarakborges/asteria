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
        var rotations = ParseRotations(root);
        var mining = ParseMining(root);
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

        var emission = OptionalByte(root, "lightEmission") ?? (byte)0;
        if (emission > 15)
        {
            throw new FormatException($"Block {id} lightEmission must be within 0..15.");
        }

        var previewColor = root.TryGetProperty("previewColor", out var previewElement)
            ? BlockPreviewColor.Parse(previewElement.GetString() ?? throw new FormatException($"Block {id} previewColor must be a string."))
            : BlockPreviewColor.Missing;

        return new BlockDefinition(
            id,
            category,
            tags,
            tint,
            textures,
            rotations,
            mining,
            isCollidable,
            renderMode,
            castsShadow,
            lightDampening,
            new BlockLightEmission(emission, emission, emission),
            previewColor);
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

    private static BlockTextureRotations ParseRotations(JsonElement root)
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

        var hardness = 1f;
        if (mining.TryGetProperty("hardness", out var hardnessElement))
        {
            if (hardnessElement.ValueKind != JsonValueKind.Number || !hardnessElement.TryGetSingle(out hardness))
            {
                throw new FormatException("mining.hardness must be a number.");
            }
        }

        return new BlockMiningDefinition(
            hardness,
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
