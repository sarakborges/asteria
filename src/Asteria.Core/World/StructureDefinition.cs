namespace Asteria.Core.World;

public enum StructureRotation
{
    Degrees0,
    Degrees90,
    Degrees180,
    Degrees270,
}

public enum StructureConnectorFace
{
    Right,
    Left,
    Top,
    Bottom,
    Front,
    Back,
}

public readonly record struct StructureConnectorAttachment(
    StructureRotation Rotation,
    int OriginX,
    int OriginY,
    int OriginZ);

public readonly record struct StructureConnectorDefinition
{
    public StructureConnectorDefinition(
        int x,
        int y,
        int z,
        StructureConnectorFace face,
        string? target = null,
        float strength = 1f,
        float strengthLossOnEachLoop = 0f,
        int minDistance = 0,
        int maxDistance = 0)
    {
        if (target is not null)
        {
            StructureDefinition.ValidateId(
                target);
        }

        if (minDistance < 0 ||
            maxDistance < minDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minDistance),
                "Structure connector distance must satisfy 0 <= min <= max.");
        }

        if (target is null &&
            (minDistance != 0 ||
             maxDistance != 0))
        {
            throw new ArgumentException(
                "Input connectors cannot define minDistance/maxDistance.");
        }

        if (target is not null &&
            (!float.IsFinite(strength) ||
             strength <= 0f ||
             strength > 1f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(strength),
                "Output connector strength must be within (0, 1].");
        }

        if (!float.IsFinite(
                strengthLossOnEachLoop) ||
            strengthLossOnEachLoop < 0f ||
            strengthLossOnEachLoop > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strengthLossOnEachLoop),
                "Connector strength loss must be within 0..1.");
        }

        X = x;
        Y = y;
        Z = z;
        Face = face;
        Target = target;
        Strength = strength;
        StrengthLossOnEachLoop =
            strengthLossOnEachLoop;
        MinDistance = minDistance;
        MaxDistance = maxDistance;
    }

    public int X { get; }

    public int Y { get; }

    public int Z { get; }

    public StructureConnectorFace Face { get; }

    public string? Target { get; }

    public float Strength { get; }

    public float StrengthLossOnEachLoop { get; }

    public int MinDistance { get; }

    public int MaxDistance { get; }
}

public readonly record struct StructureAnchor(
    int X,
    int Y,
    int Z);

public enum StructureReplacePolicy
{
    Any,
    AirOnly,
    Terrain,
}

public enum StructureFluidPolicy
{
    Displace,
    Preserve,
    Forbid,
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
            false);
}

public enum StructureProximityMode
{
    Required,
    Forbidden,
}

public sealed record StructureProximityTargetDefinition
{
    public StructureProximityTargetDefinition(
        string? block = null,
        string? fluid = null)
    {
        if ((block is null) ==
            (fluid is null))
        {
            throw new ArgumentException(
                "Structure proximity target must define exactly one of block or fluid.");
        }

        if (block is not null)
        {
            BlockDefinition.ValidateId(
                block);
        }

        if (fluid is not null)
        {
            FluidDefinition.ValidateId(
                fluid);
        }

        Block = block;
        Fluid = fluid;
    }

    public string? Block { get; }

    public string? Fluid { get; }
}

public sealed record StructureProximityRestrictionDefinition
{
    public const int MaximumDistanceLimit = 64;

    public StructureProximityRestrictionDefinition(
        StructureProximityTargetDefinition target,
        StructureProximityMode mode,
        int maxDistance,
        int? minDistance = null)
    {
        Target =
            target ??
            throw new ArgumentNullException(
                nameof(target));

        if (maxDistance is < 0 or > MaximumDistanceLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDistance),
                $"Structure proximity maxDistance must be within 0..{MaximumDistanceLimit}.");
        }

        if (minDistance is { } minimum &&
            (minimum < 0 ||
             minimum > maxDistance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minDistance),
                "Structure proximity minDistance must be non-negative and cannot exceed maxDistance.");
        }

        Mode = mode;
        MaxDistance = maxDistance;
        MinDistance = minDistance;
    }

    public StructureProximityTargetDefinition Target { get; }

    public StructureProximityMode Mode { get; }

    public int MaxDistance { get; }

    public int? MinDistance { get; }
}

public sealed class StructureRestrictionsDefinition
{
    public StructureRestrictionsDefinition(
        int maxSlope = 1,
        bool requiresDryGround = true,
        float requiredBiomeCoverage = 0f,
        IEnumerable<string>? groundBlocks = null,
        IEnumerable<StructureProximityRestrictionDefinition>? proximity = null)
    {
        if (maxSlope is < 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSlope),
                "Structure maxSlope must be within 0..64.");
        }

        if (!float.IsFinite(
                requiredBiomeCoverage) ||
            requiredBiomeCoverage is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredBiomeCoverage),
                "Structure requiredBiomeCoverage must be within 0..1.");
        }

        var authoredGroundBlocks =
            groundBlocks?.ToArray() ??
            Array.Empty<string>();
        var uniqueGroundBlocks =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var block in
                 authoredGroundBlocks)
        {
            BlockDefinition.ValidateId(
                block);

            if (!uniqueGroundBlocks.Add(
                    block))
            {
                throw new ArgumentException(
                    $"Duplicate structure ground block: {block}",
                    nameof(groundBlocks));
            }
        }

        var authoredProximity =
            proximity?.ToArray() ??
            Array.Empty<StructureProximityRestrictionDefinition>();

        for (var index = 0;
             index < authoredProximity.Length;
             index++)
        {
            var current =
                authoredProximity[index] ??
                throw new ArgumentException(
                    "Structure proximity rules cannot contain null entries.",
                    nameof(proximity));

            for (var previousIndex = 0;
                 previousIndex < index;
                 previousIndex++)
            {
                var previous =
                    authoredProximity[previousIndex];

                if (previous == current)
                {
                    throw new ArgumentException(
                        "Structure proximity rules cannot contain duplicates.",
                        nameof(proximity));
                }

                if (previous.Target == current.Target &&
                    previous.MinDistance == current.MinDistance &&
                    previous.MaxDistance == current.MaxDistance &&
                    previous.Mode != current.Mode)
                {
                    throw new ArgumentException(
                        "Structure proximity cannot require and forbid the same target over the same distance range.",
                        nameof(proximity));
                }
            }
        }

        MaxSlope = maxSlope;
        RequiresDryGround = requiresDryGround;
        RequiredBiomeCoverage = requiredBiomeCoverage;
        GroundBlocks =
            Array.AsReadOnly(
                authoredGroundBlocks);
        Proximity =
            Array.AsReadOnly(
                authoredProximity);
    }

    public int MaxSlope { get; }

    public bool RequiresDryGround { get; }

    public float RequiredBiomeCoverage { get; }

    public IReadOnlyList<string> GroundBlocks { get; }

    public IReadOnlyList<StructureProximityRestrictionDefinition>
        Proximity { get; }
}

public readonly record struct StructureVoxelDefinition(
    int X,
    int Y,
    int Z,
    string Block,
    BlockOrientation Orientation);

public sealed class StructureDefinition
{
    public const int MaximumVoxelCount = 131_072;
    public const int MaximumOffsetMagnitude = 2_048;

    public StructureDefinition(
        string id,
        bool rotation,
        StructureAnchor anchor,
        IEnumerable<StructureVoxelDefinition> voxels,
        StructureRestrictionsDefinition? restrictions = null,
        string? groupId = null,
        int priority = 0,
        IEnumerable<string>? conflictGroups = null,
        StructureGenerationDefinition? generation = null,
        IEnumerable<StructureConnectorDefinition>? connectors = null)
    {
        ValidateId(
            id);

        if (groupId is not null)
        {
            ValidateLocalId(
                groupId,
                nameof(groupId));
        }

        var authoredVoxels =
            voxels?.ToArray() ??
            throw new ArgumentNullException(
                nameof(voxels));
        if (authoredVoxels.Length == 0)
        {
            throw new ArgumentException(
                "Structure must contain at least one block voxel.",
                nameof(voxels));
        }

        if (authoredVoxels.Length >
            MaximumVoxelCount)
        {
            throw new ArgumentException(
                $"Structure may contain at most {MaximumVoxelCount} block voxels.",
                nameof(voxels));
        }

        var occupied =
            new HashSet<(int X, int Y, int Z)>();
        foreach (var voxel in authoredVoxels)
        {
            if (Math.Abs(
                    (long)voxel.X) >
                    MaximumOffsetMagnitude ||
                Math.Abs(
                    (long)voxel.Y) >
                    MaximumOffsetMagnitude ||
                Math.Abs(
                    (long)voxel.Z) >
                    MaximumOffsetMagnitude)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voxels),
                    $"Structure voxel offsets must stay within ±{MaximumOffsetMagnitude} blocks of the anchor.");
            }

            BlockDefinition.ValidateId(
                voxel.Block);
            if (!occupied.Add(
                    (
                        voxel.X,
                        voxel.Y,
                        voxel.Z)))
            {
                throw new ArgumentException(
                    $"Structure {id} repeats voxel offset ({voxel.X}, {voxel.Y}, {voxel.Z}).",
                    nameof(voxels));
            }
        }

        var authoredConnectors =
            connectors?.ToArray() ??
            Array.Empty<StructureConnectorDefinition>();
        var uniqueConnectors =
            new HashSet<(
                int X,
                int Y,
                int Z,
                StructureConnectorFace Face,
                string? Target)>();

        foreach (var connector in
                 authoredConnectors)
        {
            if (Math.Abs(
                    (long)connector.X) >
                    MaximumOffsetMagnitude ||
                Math.Abs(
                    (long)connector.Y) >
                    MaximumOffsetMagnitude ||
                Math.Abs(
                    (long)connector.Z) >
                    MaximumOffsetMagnitude)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectors),
                    $"Structure connector offsets must stay within ±{MaximumOffsetMagnitude} blocks of the anchor.");
            }

            if (!uniqueConnectors.Add(
                    (
                        connector.X,
                        connector.Y,
                        connector.Z,
                        connector.Face,
                        connector.Target)))
            {
                throw new ArgumentException(
                    $"Structure {id} repeats connector at ({connector.X}, {connector.Y}, {connector.Z}) face {connector.Face} target {connector.Target ?? "<input>"}.",
                    nameof(connectors));
            }
        }

        var authoredConflictGroups =
            conflictGroups?.ToArray() ??
            Array.Empty<string>();
        var uniqueConflictGroups =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var group in
                 authoredConflictGroups)
        {
            ValidateLocalId(
                group,
                nameof(conflictGroups));

            if (!uniqueConflictGroups.Add(
                    group))
            {
                throw new ArgumentException(
                    $"Duplicate structure conflict group: {group}",
                    nameof(conflictGroups));
            }
        }

        Id = id;
        Rotation = rotation;
        Anchor = anchor;
        Restrictions =
            restrictions ??
            new StructureRestrictionsDefinition();
        GroupId = groupId;
        Priority = priority;
        ConflictGroups =
            Array.AsReadOnly(
                authoredConflictGroups);
        Generation =
            generation ??
            StructureGenerationDefinition.Default;
        Voxels =
            Array.AsReadOnly(
                authoredVoxels);

        Connectors =
            Array.AsReadOnly(
                authoredConnectors);

        MinimumX =
            authoredVoxels.Min(
                voxel =>
                    voxel.X);
        MaximumX =
            authoredVoxels.Max(
                voxel =>
                    voxel.X);
        MinimumY =
            authoredVoxels.Min(
                voxel =>
                    voxel.Y);
        MaximumY =
            authoredVoxels.Max(
                voxel =>
                    voxel.Y);
        MinimumZ =
            authoredVoxels.Min(
                voxel =>
                    voxel.Z);
        MaximumZ =
            authoredVoxels.Max(
                voxel =>
                    voxel.Z);
    }

    public string Id { get; }

    public string? GroupId { get; }

    public int Priority { get; }

    public IReadOnlyList<string> ConflictGroups { get; }

    public StructureGenerationDefinition Generation { get; }

    public bool Rotation { get; }

    public StructureAnchor Anchor { get; }

    public StructureRestrictionsDefinition Restrictions { get; }

    public IReadOnlyList<StructureVoxelDefinition> Voxels { get; }

    public IReadOnlyList<StructureConnectorDefinition>
        Connectors { get; }

    public int MinimumX { get; }

    public int MaximumX { get; }

    public int MinimumY { get; }

    public int MaximumY { get; }

    public int MinimumZ { get; }

    public int MaximumZ { get; }

    public string? GroupReference
    {
        get
        {
            if (GroupId is null)
            {
                return null;
            }

            var separator =
                Id.IndexOf(
                    ':');
            return Id[..separator] +
                   ":" +
                   GroupId;
        }
    }

    public StructureRotation RotationForHash(
        ulong hash)
    {
        if (!Rotation)
        {
            return StructureRotation.Degrees0;
        }

        return (hash >> 32 & 3UL) switch
        {
            1 => StructureRotation.Degrees90,
            2 => StructureRotation.Degrees180,
            3 => StructureRotation.Degrees270,
            _ => StructureRotation.Degrees0,
        };
    }

    public static (
        int X,
        int Y,
        int Z)
        RotateOffset(
            StructureRotation rotation,
            int x,
            int y,
            int z) =>
        rotation switch
        {
            StructureRotation.Degrees90 =>
                (
                    -z,
                    y,
                    x),
            StructureRotation.Degrees180 =>
                (
                    -x,
                    y,
                    -z),
            StructureRotation.Degrees270 =>
                (
                    z,
                    y,
                    -x),
            _ =>
                (
                    x,
                    y,
                    z),
        };

    public static BlockOrientation RotateOrientation(
        StructureRotation rotation,
        BlockOrientation orientation) =>
        rotation is
            StructureRotation.Degrees90 or
            StructureRotation.Degrees270
            ? orientation switch
            {
                BlockOrientation.X =>
                    BlockOrientation.Z,
                BlockOrientation.Z =>
                    BlockOrientation.X,
                _ =>
                    orientation,
            }
            : orientation;

    public IReadOnlyList<StructureRotation>
        SupportedRotations() =>
        Rotation
            ? Enum.GetValues<StructureRotation>()
            : [
                StructureRotation.Degrees0,
            ];

    public static StructureConnectorFace
        RotateConnectorFace(
            StructureRotation rotation,
            StructureConnectorFace face) =>
        rotation switch
        {
            StructureRotation.Degrees90 =>
                face switch
                {
                    StructureConnectorFace.Right =>
                        StructureConnectorFace.Front,
                    StructureConnectorFace.Front =>
                        StructureConnectorFace.Left,
                    StructureConnectorFace.Left =>
                        StructureConnectorFace.Back,
                    StructureConnectorFace.Back =>
                        StructureConnectorFace.Right,
                    _ =>
                        face,
                },
            StructureRotation.Degrees180 =>
                face switch
                {
                    StructureConnectorFace.Right =>
                        StructureConnectorFace.Left,
                    StructureConnectorFace.Left =>
                        StructureConnectorFace.Right,
                    StructureConnectorFace.Front =>
                        StructureConnectorFace.Back,
                    StructureConnectorFace.Back =>
                        StructureConnectorFace.Front,
                    _ =>
                        face,
                },
            StructureRotation.Degrees270 =>
                face switch
                {
                    StructureConnectorFace.Right =>
                        StructureConnectorFace.Back,
                    StructureConnectorFace.Back =>
                        StructureConnectorFace.Left,
                    StructureConnectorFace.Left =>
                        StructureConnectorFace.Front,
                    StructureConnectorFace.Front =>
                        StructureConnectorFace.Right,
                    _ =>
                        face,
                },
            _ =>
                face,
        };

    public static StructureConnectorFace
        OppositeConnectorFace(
            StructureConnectorFace face) =>
        face switch
        {
            StructureConnectorFace.Right =>
                StructureConnectorFace.Left,
            StructureConnectorFace.Left =>
                StructureConnectorFace.Right,
            StructureConnectorFace.Top =>
                StructureConnectorFace.Bottom,
            StructureConnectorFace.Bottom =>
                StructureConnectorFace.Top,
            StructureConnectorFace.Front =>
                StructureConnectorFace.Back,
            StructureConnectorFace.Back =>
                StructureConnectorFace.Front,
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(face)),
        };

    public static (
        int X,
        int Y,
        int Z)
        ConnectorFaceOffset(
            StructureConnectorFace face) =>
        face switch
        {
            StructureConnectorFace.Right =>
                (1, 0, 0),
            StructureConnectorFace.Left =>
                (-1, 0, 0),
            StructureConnectorFace.Top =>
                (0, 1, 0),
            StructureConnectorFace.Bottom =>
                (0, -1, 0),
            StructureConnectorFace.Front =>
                (0, 0, 1),
            StructureConnectorFace.Back =>
                (0, 0, -1),
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(face)),
        };

    public IReadOnlyList<StructureConnectorAttachment>
        CompatibleInputAttachments(
            int worldX,
            int worldY,
            int worldZ,
            StructureConnectorFace requiredWorldFace)
    {
        var candidates =
            new List<(
                StructureConnectorAttachment Attachment,
                int ConnectorIndex)>();

        for (var connectorIndex = 0;
             connectorIndex < Connectors.Count;
             connectorIndex++)
        {
            var connector =
                Connectors[connectorIndex];
            if (connector.Target is not null)
            {
                continue;
            }

            foreach (var rotation in
                     SupportedRotations())
            {
                if (RotateConnectorFace(
                        rotation,
                        connector.Face) !=
                    requiredWorldFace)
                {
                    continue;
                }

                var rotated =
                    RotateOffset(
                        rotation,
                        connector.X,
                        connector.Y,
                        connector.Z);
                candidates.Add(
                    (
                        new StructureConnectorAttachment(
                            rotation,
                            checked(
                                worldX -
                                rotated.X),
                            checked(
                                worldY -
                                rotated.Y),
                            checked(
                                worldZ -
                                rotated.Z)),
                        connectorIndex));
            }
        }

        candidates.Sort(
            (left, right) =>
            {
                var byRotation =
                    left.Attachment
                        .Rotation
                        .CompareTo(
                            right.Attachment.Rotation);
                if (byRotation != 0)
                {
                    return byRotation;
                }

                var byX =
                    left.Attachment
                        .OriginX
                        .CompareTo(
                            right.Attachment.OriginX);
                if (byX != 0)
                {
                    return byX;
                }

                var byY =
                    left.Attachment
                        .OriginY
                        .CompareTo(
                            right.Attachment.OriginY);
                if (byY != 0)
                {
                    return byY;
                }

                var byZ =
                    left.Attachment
                        .OriginZ
                        .CompareTo(
                            right.Attachment.OriginZ);
                return byZ != 0
                    ? byZ
                    : left.ConnectorIndex
                        .CompareTo(
                            right.ConnectorIndex);
            });

        return Array.AsReadOnly(
            candidates
                .Select(value =>
                    value.Attachment)
                .ToArray());
    }

    public StructureConnectorAttachment?
        ResolveInputAttachment(
            int worldX,
            int worldY,
            int worldZ,
            StructureConnectorFace requiredWorldFace,
            ulong hash)
    {
        var candidates =
            CompatibleInputAttachments(
                worldX,
                worldY,
                worldZ,
                requiredWorldFace);
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[
            checked((int)(
                hash %
                (ulong)candidates.Count))];
    }

    internal static void ValidateId(
        string id) =>
        BiomeDefinition.ValidateId(
            id);

    private static void ValidateLocalId(
        string id,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                id) ||
            id != id.Trim() ||
            id.Contains(
                ':',
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Structure local id must be non-empty, trimmed, and unnamespaced.",
                parameterName);
        }

        foreach (var character in id)
        {
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is '/' or '_' or '-' or '.';

            if (!valid)
            {
                throw new ArgumentException(
                    $"Invalid structure local id character '{character}' in {id}.",
                    parameterName);
            }
        }

        if (id.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Structure local id cannot contain '..': {id}",
                parameterName);
        }
    }

}
