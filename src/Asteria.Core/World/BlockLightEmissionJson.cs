using System.Text.Json;

namespace Asteria.Core.World;

/// <summary>Shared authored RGB emission contract for blocks and Objects.</summary>
public static class BlockLightEmissionJson
{
    public static BlockLightEmission Parse(JsonElement root, string contentId)
    {
        if (!root.TryGetProperty("lightEmission", out var emission) ||
            emission.ValueKind == JsonValueKind.Null)
            return default;

        if (emission.ValueKind == JsonValueKind.Number)
        {
            if (!emission.TryGetByte(out var level) || level > VoxelLight.MaxLevel)
                throw new FormatException(
                    $"{contentId} lightEmission must be within 0..15.");
            return new BlockLightEmission(level, level, level);
        }

        if (emission.ValueKind != JsonValueKind.Object)
            throw new FormatException(
                $"{contentId} lightEmission must be a 0..15 number or RGB object.");

        return new BlockLightEmission(
            RequiredChannel(emission, "red", contentId),
            RequiredChannel(emission, "green", contentId),
            RequiredChannel(emission, "blue", contentId));
    }

    private static byte RequiredChannel(JsonElement emission, string channel, string id)
    {
        if (!emission.TryGetProperty(channel, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetByte(out var level) || level > VoxelLight.MaxLevel)
            throw new FormatException(
                $"{id} lightEmission.{channel} must be within 0..15.");
        return level;
    }
}
