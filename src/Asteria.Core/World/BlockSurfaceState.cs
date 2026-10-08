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
/// Immutable portable surface state. Absence is represented by Empty; per
/// chunk storage remains sparse. Order is attachment order, so Shears remove
/// the final layer on the hit face, not an intrinsic block texture.
/// </summary>
public sealed class BlockSurfaceState : IEquatable<BlockSurfaceState>
{
    public const int MaximumAttachedLayers = 16;

    private readonly AttachedBlockLayer[] _layers;
    private readonly IReadOnlyList<AttachedBlockLayer> _view;

    public static BlockSurfaceState Empty { get; } = new(null, []);

    public BlockSurfaceState(
        string? dyeId,
        IEnumerable<AttachedBlockLayer>? layers = null)
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
    }

    public string? DyeId { get; }
    public IReadOnlyList<AttachedBlockLayer> Layers => _view;
    public bool IsEmpty => DyeId is null && _layers.Length == 0;

    public BlockSurfaceState WithDye(string? dyeId) =>
        StringComparer.Ordinal.Equals(DyeId, dyeId)
            ? this
            : new BlockSurfaceState(dyeId, _layers);

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

        updated = new BlockSurfaceState(DyeId, _layers.Append(layer));
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
            updated = new BlockSurfaceState(DyeId, copy);
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
         _layers.AsSpan().SequenceEqual(other._layers));

    public override bool Equals(object? obj) => obj is BlockSurfaceState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DyeId, StringComparer.Ordinal);
        foreach (var layer in _layers)
            hash.Add(layer);
        return hash.ToHashCode();
    }
}
