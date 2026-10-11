using System.Collections.ObjectModel;

namespace Asteria.Core.World;

/// <summary>
/// An authored layer attached to one face of a block. This is independent of
/// the block's intrinsic base/overlay textures.
/// </summary>
public readonly record struct AttachedBlockLayer
{
    public AttachedBlockLayer(
        BlockFace face,
        string layerId,
        TextureRotation rotation = TextureRotation.Degrees0)
    {
        if (!Enum.IsDefined(face))
            throw new ArgumentOutOfRangeException(nameof(face));
        BlockDefinition.ValidateId(layerId);
        if (!Enum.IsDefined(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation));

        Face = face;
        LayerId = layerId;
        Rotation = rotation;
    }

    public BlockFace Face { get; }
    public string LayerId { get; }
    public TextureRotation Rotation { get; }
}

/// <summary>
/// Immutable portable per-voxel extra state: dye, face attachments and
/// bounded arbitrary metadata tags. Absence is represented by Empty and
/// per-chunk storage remains sparse. Order is attachment order; mining
/// resolves the final layer on the hit face before reaching the host block.
/// </summary>
public sealed class BlockSurfaceState : IEquatable<BlockSurfaceState>
{
    public const int MaximumAttachedLayers = 16;
    public const int MaximumMetaTags = 8;
    public const int MaximumMetaTagKeyLength = 64;
    public const int MaximumMetaTagValueLength = 256;

    private readonly AttachedBlockLayer[] _layers;
    private readonly IReadOnlyList<AttachedBlockLayer> _view;
    private readonly IReadOnlyDictionary<string, string> _metaTags;

    public static BlockSurfaceState Empty { get; } = new(null, []);

    public BlockSurfaceState(
        string? dyeId,
        IEnumerable<AttachedBlockLayer>? layers = null,
        IReadOnlyDictionary<string, string>? metaTags = null)
    {
        if (dyeId is not null)
            BlockDefinition.ValidateId(dyeId);

        DyeId = dyeId;
        _layers = layers?.ToArray() ?? [];
        if (_layers.Length > MaximumAttachedLayers)
            throw new ArgumentOutOfRangeException(
                nameof(layers), "A voxel cannot have more than 16 attached layers.");

        var unique = new HashSet<(BlockFace, string)>();
        foreach (var layer in _layers)
        {
            if (!Enum.IsDefined(layer.Face) || string.IsNullOrWhiteSpace(layer.LayerId))
                throw new ArgumentException("Invalid attached layer.", nameof(layers));
            BlockDefinition.ValidateId(layer.LayerId);
            if (!unique.Add((layer.Face, layer.LayerId)))
                throw new ArgumentException("A layer cannot be attached twice to the same face.", nameof(layers));
        }

        _view = Array.AsReadOnly(_layers);
        if (metaTags is { Count: > MaximumMetaTags })
            throw new ArgumentOutOfRangeException(
                nameof(metaTags), "A block supports at most eight metadata tags.");

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metaTags is not null)
        foreach (var (key, value) in metaTags)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                key.Length > MaximumMetaTagKeyLength ||
                key.Any(char.IsWhiteSpace) ||
                string.IsNullOrWhiteSpace(value) ||
                value.Length > MaximumMetaTagValueLength ||
                !copy.TryAdd(key, value))
                throw new ArgumentException(
                    "Block metadata keys and values must be bounded and unique.",
                    nameof(metaTags));
        }
        _metaTags = new ReadOnlyDictionary<string, string>(copy);
    }

    public string? DyeId { get; }
    public IReadOnlyList<AttachedBlockLayer> Layers => _view;
    public IReadOnlyDictionary<string, string> MetaTags => _metaTags;
    public bool IsEmpty => DyeId is null && _layers.Length == 0 &&
                           _metaTags.Count == 0;

    public BlockSurfaceState WithDye(string? dyeId) =>
        StringComparer.Ordinal.Equals(DyeId, dyeId)
            ? this
            : new BlockSurfaceState(dyeId, _layers, _metaTags);

    public BlockSurfaceState WithMetaTag(string key, string? value)
    {
        var copy = new Dictionary<string, string>(_metaTags, StringComparer.Ordinal);
        if (value is null)
        {
            if (!copy.Remove(key)) return this;
        }
        else
        {
            if (copy.TryGetValue(key, out var old) &&
                StringComparer.Ordinal.Equals(old, value))
                return this;
            copy[key] = value;
        }
        return new BlockSurfaceState(DyeId, _layers, copy);
    }

    public bool TryAttach(AttachedBlockLayer layer, out BlockSurfaceState updated)
    {
        if (_layers.Length == MaximumAttachedLayers ||
            _layers.Any(existing =>
                existing.Face == layer.Face &&
                StringComparer.Ordinal.Equals(existing.LayerId, layer.LayerId)))
        {
            updated = this;
            return false;
        }

        updated = new BlockSurfaceState(DyeId, _layers.Append(layer), _metaTags);
        return true;
    }

    public bool TryGetTop(BlockFace face, out AttachedBlockLayer top)
    {
        for (var index = _layers.Length - 1; index >= 0; index--)
        {
            if (_layers[index].Face != face) continue;
            top = _layers[index];
            return true;
        }
        top = default;
        return false;
    }

    public bool TryRemoveEverywhere(string id, out BlockSurfaceState updated)
    {
        BlockDefinition.ValidateId(id);
        var kept = _layers.Where(layer =>
            !StringComparer.Ordinal.Equals(layer.LayerId, id)).ToArray();
        if (kept.Length == _layers.Length)
        {
            updated = this;
            return false;
        }
        updated = new BlockSurfaceState(DyeId, kept, _metaTags);
        return true;
    }

    public bool TryRemoveTop(
        BlockFace face,
        out BlockSurfaceState updated,
        out AttachedBlockLayer removed)
    {
        if (!Enum.IsDefined(face))
            throw new ArgumentOutOfRangeException(nameof(face));

        for (var index = _layers.Length - 1; index >= 0; index--)
        {
            if (_layers[index].Face != face)
                continue;

            removed = _layers[index];
            var copy = new AttachedBlockLayer[_layers.Length - 1];
            Array.Copy(_layers, 0, copy, 0, index);
            Array.Copy(_layers, index + 1, copy, index, _layers.Length - index - 1);
            updated = new BlockSurfaceState(DyeId, copy, _metaTags);
            return true;
        }

        updated = this;
        removed = default;
        return false;
    }

    public bool Equals(BlockSurfaceState? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
         StringComparer.Ordinal.Equals(DyeId, other.DyeId) &&
         _layers.AsSpan().SequenceEqual(other._layers) &&
         _metaTags.Count == other._metaTags.Count &&
         _metaTags.All(pair => other._metaTags.TryGetValue(pair.Key, out var value) &&
                               StringComparer.Ordinal.Equals(pair.Value, value)));

    public override bool Equals(object? obj) => obj is BlockSurfaceState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DyeId, StringComparer.Ordinal);
        foreach (var layer in _layers)
            hash.Add(layer);
        foreach (var (key, value) in _metaTags.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
