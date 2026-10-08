using System.Text.Json;

namespace Asteria.Core.World;

/// <summary>Strict shared JSON contract for optional structure block state.</summary>
public static class StructureVoxelStateJson
{
    public static StructureVoxelState? Parse(JsonElement entry)
    {
        static string? Text(JsonElement element, string key)
        {
            if (!element.TryGetProperty(key, out var value)) return null;
            if (value.ValueKind != JsonValueKind.String)
                throw new FormatException($"Structure {key} must be a string.");
            return value.GetString();
        }

        var rotation = Text(entry, "textureRotation") switch
        {
            null or "0" => TextureRotation.Degrees0,
            "90" => TextureRotation.Degrees90,
            "180" => TextureRotation.Degrees180,
            "270" => TextureRotation.Degrees270,
            _ => throw new FormatException("Invalid structure texture rotation.")
        };
        var facing = Text(entry, "facing") switch
        {
            null or "south" => HorizontalFacing.South,
            "north" => HorizontalFacing.North,
            "east" => HorizontalFacing.East,
            "west" => HorizontalFacing.West,
            _ => throw new FormatException("Invalid structure horizontal facing.")
        };
        ushort state = 0;
        if (entry.TryGetProperty("state", out var rawState) &&
            (rawState.ValueKind != JsonValueKind.Number ||
             !rawState.TryGetUInt16(out state)))
            throw new FormatException("Structure state must be an unsigned 16-bit integer.");
        var maskText = Text(entry, "microblocks");
        var mask = maskText is null
            ? MicroblockMask.Empty : MicroblockMask.ParseHexString(maskText);
        var dye = Text(entry, "dye");
        var layers = new List<AttachedBlockLayer>();
        if (entry.TryGetProperty("attachments", out var rawAttachments))
        {
            if (rawAttachments.ValueKind != JsonValueKind.Array ||
                rawAttachments.GetArrayLength() > BlockSurfaceState.MaximumAttachedLayers)
                throw new FormatException("Structure attachments must be an array of at most 16 entries.");
            foreach (var attachment in rawAttachments.EnumerateArray())
            {
                if (attachment.ValueKind != JsonValueKind.Object ||
                    attachment.EnumerateObject().Any(x => x.Name is not ("face" or "layer" or "rotation")))
                    throw new FormatException("Invalid structure attachment fields.");
                var face = Text(attachment, "face") switch
                {
                    "right" => BlockFace.Right,
                    "left" => BlockFace.Left,
                    "top" => BlockFace.Top,
                    "bottom" => BlockFace.Bottom,
                    "front" => BlockFace.Front,
                    "back" => BlockFace.Back,
                    _ => throw new FormatException("Invalid structure attachment face.")
                };
                var layer = Text(attachment, "layer") ??
                    throw new FormatException("Structure attachment requires layer ID.");
                var angle = Text(attachment, "rotation") switch
                {
                    null or "0" => TextureRotation.Degrees0,
                    "90" => TextureRotation.Degrees90,
                    "180" => TextureRotation.Degrees180,
                    "270" => TextureRotation.Degrees270,
                    _ => throw new FormatException("Invalid structure attachment rotation.")
                };
                layers.Add(new AttachedBlockLayer(face, layer, angle));
            }
        }
        var surface = new BlockSurfaceState(dye, layers);
        var result = new StructureVoxelState(rotation, facing, state, mask, surface);
        return result.IsDefault ? null : result;
    }

    public static IReadOnlyDictionary<string, object> PaletteEntry(
        string block, BlockOrientation orientation, StructureVoxelState? detail)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["block"] = block,
            ["orientation"] = orientation switch
            {
                BlockOrientation.X => "x",
                BlockOrientation.Y => "y",
                BlockOrientation.Z => "z",
                _ => throw new ArgumentOutOfRangeException(nameof(orientation))
            }
        };
        if (detail is null || detail.IsDefault) return result;
        if (detail.TextureRotation != TextureRotation.Degrees0)
            result["textureRotation"] = ((int)detail.TextureRotation * 90).ToString();
        if (detail.Facing != HorizontalFacing.South)
            result["facing"] = detail.Facing.ToString().ToLowerInvariant();
        if (detail.State != 0) result["state"] = detail.State;
        if (!detail.Mask.IsEmpty) result["microblocks"] = detail.Mask.ToHexString();
        if (detail.Surface.DyeId is { } dye) result["dye"] = dye;
        if (detail.Surface.Layers.Count > 0)
            result["attachments"] = detail.Surface.Layers.Select(layer => new
            {
                face = layer.Face.ToString().ToLowerInvariant(),
                layer = layer.LayerId,
                rotation = ((int)layer.Rotation * 90).ToString()
            }).ToArray();
        return result;
    }
}
