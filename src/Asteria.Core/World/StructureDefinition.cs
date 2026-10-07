namespace Asteria.Core.World;

public enum StructureRotation
{
    Degrees0,
    Degrees90,
    Degrees180,
    Degrees270,
}

public readonly record struct StructureAnchor(
    int X,
    int Y,
    int Z);

public sealed class StructureRestrictionsDefinition
{
    public StructureRestrictionsDefinition(
        int maxSlope = 1,
        bool requiresDryGround = true,
        float requiredBiomeCoverage = 0f)
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

        MaxSlope = maxSlope;
        RequiresDryGround = requiresDryGround;
        RequiredBiomeCoverage = requiredBiomeCoverage;
    }

    public int MaxSlope { get; }

    public bool RequiresDryGround { get; }

    public float RequiredBiomeCoverage { get; }
}

public readonly record struct StructureVoxelDefinition(
    int X,
    int Y,
    int Z,
    string Block,
    BlockOrientation Orientation);

public sealed class StructureDefinition
{
    public StructureDefinition(
        string id,
        bool rotation,
        StructureAnchor anchor,
        IEnumerable<StructureVoxelDefinition> voxels,
        StructureRestrictionsDefinition? restrictions = null,
        string? groupId = null)
    {
        ValidateId(
            id);

        if (groupId is not null &&
            (string.IsNullOrWhiteSpace(groupId) ||
             groupId != groupId.Trim() ||
             groupId.Contains(
                 ':',
                 StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Structure groupId must be a trimmed local id without a namespace.",
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

        var occupied =
            new HashSet<(int X, int Y, int Z)>();
        foreach (var voxel in authoredVoxels)
        {
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

        Id = id;
        Rotation = rotation;
        Anchor = anchor;
        Restrictions =
            restrictions ??
            new StructureRestrictionsDefinition();
        GroupId = groupId;
        Voxels =
            Array.AsReadOnly(
                authoredVoxels);

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

    public bool Rotation { get; }

    public StructureAnchor Anchor { get; }

    public StructureRestrictionsDefinition Restrictions { get; }

    public IReadOnlyList<StructureVoxelDefinition> Voxels { get; }

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

    internal static void ValidateId(
        string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id != id.Trim())
        {
            throw new ArgumentException(
                "Structure id must be non-empty and trimmed.",
                nameof(id));
        }

        var separator =
            id.IndexOf(
                ':');
        if (separator <= 0 ||
            separator ==
                id.Length - 1 ||
            id.IndexOf(
                ':',
                separator + 1) >=
            0)
        {
            throw new ArgumentException(
                "Structure id must use namespace:name.",
                nameof(id));
        }
    }
}
