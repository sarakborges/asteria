namespace Asteria.Core.World;

public enum StructureRotation : byte
{
    Degrees0 = 0,
    Degrees90 = 1,
    Degrees180 = 2,
    Degrees270 = 3,
}

public enum StructureReplacePolicy : byte
{
    Any = 0,
    AirOnly = 1,
    Terrain = 2,
}

public enum StructureFluidPolicy : byte
{
    Displace = 0,
    Preserve = 1,
    Forbid = 2,
}

public readonly record struct StructureOffset(int X, int Y, int Z)
{
    public StructureOffset Rotate(StructureRotation rotation) =>
        rotation switch
        {
            StructureRotation.Degrees0 => this,
            StructureRotation.Degrees90 => new StructureOffset(-Z, Y, X),
            StructureRotation.Degrees180 => new StructureOffset(-X, Y, -Z),
            StructureRotation.Degrees270 => new StructureOffset(Z, Y, -X),
            _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
        };
}

public readonly record struct StructureHorizontalOffset(int X, int Z)
{
    public StructureHorizontalOffset Rotate(StructureRotation rotation)
    {
        var rotated = new StructureOffset(X, 0, Z).Rotate(rotation);
        return new StructureHorizontalOffset(rotated.X, rotated.Z);
    }
}

public sealed record StructureVoxel(
    StructureOffset Offset,
    string Block,
    BlockOrientation Orientation)
{
    public StructureVoxel Rotate(StructureRotation rotation)
    {
        var orientation =
            rotation is StructureRotation.Degrees90 or StructureRotation.Degrees270
                ? Orientation switch
                {
                    BlockOrientation.X => BlockOrientation.Z,
                    BlockOrientation.Z => BlockOrientation.X,
                    _ => Orientation,
                }
                : Orientation;

        return this with
        {
            Offset = Offset.Rotate(rotation),
            Orientation = orientation,
        };
    }
}

public sealed class StructureRestrictions
{
    public StructureRestrictions(
        IEnumerable<string>? groundBlocks = null,
        int minSlope = 0,
        int maxSlope = 1,
        bool requiresDryGround = true,
        float requiredBiomeCoverage = 0f)
    {
        if (minSlope < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSlope));
        }

        if (maxSlope < minSlope)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSlope));
        }

        if (!float.IsFinite(requiredBiomeCoverage) ||
            requiredBiomeCoverage < 0f ||
            requiredBiomeCoverage > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredBiomeCoverage));
        }

        var blocks = groundBlocks?.ToArray() ?? Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            BiomeSurfaceLayerDefinition.ValidateBlockId(block);
            if (!seen.Add(block))
            {
                throw new ArgumentException(
                    $"Duplicate structure ground block: {block}",
                    nameof(groundBlocks));
            }
        }

        GroundBlocks = Array.AsReadOnly(blocks);
        MinSlope = minSlope;
        MaxSlope = maxSlope;
        RequiresDryGround = requiresDryGround;
        RequiredBiomeCoverage = requiredBiomeCoverage;
    }

    public IReadOnlyList<string> GroundBlocks { get; }

    public int MinSlope { get; }

    public int MaxSlope { get; }

    public bool RequiresDryGround { get; }

    public float RequiredBiomeCoverage { get; }
}

public sealed record StructureGenerationDefinition(
    StructureReplacePolicy ReplacePolicy,
    StructureFluidPolicy FluidPolicy,
    bool ReserveSpace)
{
    public static StructureGenerationDefinition Default { get; } =
        new(
            StructureReplacePolicy.Any,
            StructureFluidPolicy.Displace,
            ReserveSpace: false);
}

public sealed class StructureDefinition
{
    private readonly StructureVoxel[] _voxels;
    private readonly StructureHorizontalOffset[] _footprint;
    private readonly StructureHorizontalOffset[] _supports;

    public StructureDefinition(
        string id,
        string? groupId,
        bool rotation,
        int priority,
        IEnumerable<string>? conflictGroups,
        StructureRestrictions restrictions,
        StructureGenerationDefinition generation,
        IEnumerable<StructureVoxel> voxels)
    {
        BiomeDefinition.ValidateId(id);
        ArgumentNullException.ThrowIfNull(restrictions);
        ArgumentNullException.ThrowIfNull(generation);

        if (groupId is not null &&
            string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException(
                "Structure group id cannot be empty.",
                nameof(groupId));
        }

        var authoredVoxels =
            voxels?.ToArray() ??
            throw new ArgumentNullException(nameof(voxels));

        if (authoredVoxels.Length == 0)
        {
            throw new ArgumentException(
                "Structure must contain at least one block voxel.",
                nameof(voxels));
        }

        var positions = new HashSet<StructureOffset>();
        foreach (var voxel in authoredVoxels)
        {
            BiomeSurfaceLayerDefinition.ValidateBlockId(voxel.Block);
            if (!positions.Add(voxel.Offset))
            {
                throw new ArgumentException(
                    $"Structure {id} repeats voxel offset {voxel.Offset}.",
                    nameof(voxels));
            }
        }

        var conflicts = conflictGroups?.ToArray() ?? Array.Empty<string>();
        var conflictSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in conflicts)
        {
            if (string.IsNullOrWhiteSpace(group) ||
                !conflictSeen.Add(group))
            {
                throw new ArgumentException(
                    $"Structure {id} has an invalid or duplicate conflict group.",
                    nameof(conflictGroups));
            }
        }

        Id = id;
        GroupId = groupId;
        Rotation = rotation;
        Priority = priority;
        ConflictGroups = Array.AsReadOnly(conflicts);
        Restrictions = restrictions;
        Generation = generation;
        _voxels = authoredVoxels
            .OrderBy(voxel => voxel.Offset.Y)
            .ThenBy(voxel => voxel.Offset.Z)
            .ThenBy(voxel => voxel.Offset.X)
            .ToArray();

        MinimumYOffset = _voxels.Min(voxel => voxel.Offset.Y);
        MaximumYOffset = _voxels.Max(voxel => voxel.Offset.Y);

        _footprint = _voxels
            .Select(voxel => new StructureHorizontalOffset(
                voxel.Offset.X,
                voxel.Offset.Z))
            .Distinct()
            .OrderBy(offset => offset.Z)
            .ThenBy(offset => offset.X)
            .ToArray();

        _supports = _voxels
            .Where(voxel => voxel.Offset.Y == MinimumYOffset)
            .Select(voxel => new StructureHorizontalOffset(
                voxel.Offset.X,
                voxel.Offset.Z))
            .Distinct()
            .OrderBy(offset => offset.Z)
            .ThenBy(offset => offset.X)
            .ToArray();

        if (_supports.Length == 0)
        {
            throw new InvalidOperationException(
                $"Structure {id} has no support footprint.");
        }
    }

    public string Id { get; }

    public string? GroupId { get; }

    public bool Rotation { get; }

    public int Priority { get; }

    public IReadOnlyList<string> ConflictGroups { get; }

    public StructureRestrictions Restrictions { get; }

    public StructureGenerationDefinition Generation { get; }

    public int MinimumYOffset { get; }

    public int MaximumYOffset { get; }

    public IReadOnlyList<StructureVoxel> Voxels => _voxels;

    public IEnumerable<StructureRotation> SupportedRotations =>
        Rotation
            ? Enum.GetValues<StructureRotation>()
            : new[] { StructureRotation.Degrees0 };

    public IEnumerable<StructureVoxel> RotatedVoxels(
        StructureRotation rotation)
    {
        if (!Rotation &&
            rotation != StructureRotation.Degrees0)
        {
            throw new ArgumentException(
                $"Structure {Id} does not support rotation.",
                nameof(rotation));
        }

        return _voxels.Select(voxel => voxel.Rotate(rotation));
    }

    public IReadOnlyList<StructureHorizontalOffset> Footprint(
        StructureRotation rotation) =>
        _footprint
            .Select(offset => offset.Rotate(rotation))
            .Distinct()
            .ToArray();

    public IReadOnlyList<StructureHorizontalOffset> Supports(
        StructureRotation rotation) =>
        _supports
            .Select(offset => offset.Rotate(rotation))
            .Distinct()
            .ToArray();

    public (StructureHorizontalOffset Minimum, StructureHorizontalOffset Maximum)
        HorizontalBounds(StructureRotation rotation)
    {
        var footprint = Footprint(rotation);
        return (
            new StructureHorizontalOffset(
                footprint.Min(offset => offset.X),
                footprint.Min(offset => offset.Z)),
            new StructureHorizontalOffset(
                footprint.Max(offset => offset.X),
                footprint.Max(offset => offset.Z)));
    }

    public int MaximumHorizontalExtent
    {
        get
        {
            var extent = 0;

            foreach (var rotation in SupportedRotations)
            {
                var (minimum, maximum) = HorizontalBounds(rotation);
                extent = Math.Max(
                    extent,
                    Math.Max(
                        Math.Max(Math.Abs(minimum.X), Math.Abs(maximum.X)),
                        Math.Max(Math.Abs(minimum.Z), Math.Abs(maximum.Z))));
            }

            return extent;
        }
    }
}
