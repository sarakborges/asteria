namespace Asteria.Core.World;

public sealed class MeshDependencyStamp
{
    internal MeshDependencyStamp(
        ChunkContentStamp content,
        ChunkResidencyStamp residency)
    {
        Content =
            content ??
            throw new ArgumentNullException(nameof(content));
        Residency =
            residency ??
            throw new ArgumentNullException(nameof(residency));
    }

    public ChunkContentStamp Content { get; }

    public ChunkResidencyStamp Residency { get; }

    // Content and chunk-residency stamps fully describe the world geometry
    // inputs captured for a mesh worker. Lighting-only changes do not
    // change either stamp, so the published physics shape can be reused.
    public bool HasSameWorldGeometryInputsAs(
        MeshDependencyStamp other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Content.HasSameEntriesAs(other.Content) &&
               Residency.HasSameEntriesAs(other.Residency);
    }

    public bool IsCurrent(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.IsContentStampCurrent(Content) &&
               world.IsResidencyStampCurrent(Residency);
    }

    /// <summary>
    /// Only for already published meshes reused after chunk archive/restore.
    /// Fresh workers MUST use IsCurrent: epochs protect against in-flight
    /// replacement. Archived session chunks keep their voxel data unchanged,
    /// so a new residency epoch alone cannot invalidate their geometry.
    /// Presence transitions or content changes still require remeshing.
    /// Lighting is updated independently by the lighting runtime.
    /// </summary>
    public bool HasUnchangedGeometryAfterRestore(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var (coord, previousEpoch) in Residency.Entries)
        {
            if ((previousEpoch != 0) != world.ContainsChunk(coord))
                return false;
        }

        foreach (var (coord, previous) in Content.Entries)
        {
            if (!world.TryGetChunk(coord, out var chunk) ||
                chunk.Revision != previous.ChunkRevision)
                return false;
        }

        return true;
    }
}

public sealed class MeshWorkerSnapshot
{
    internal MeshWorkerSnapshot(
        VoxelWorld world,
        MeshDependencyStamp dependencies,
        IReadOnlyDictionary<ChunkMeshletKey, MeshDependencyStamp>
            meshletDependencies)
    {
        World =
            world ??
            throw new ArgumentNullException(nameof(world));
        Dependencies =
            dependencies ??
            throw new ArgumentNullException(nameof(dependencies));
        MeshletDependencies =
            meshletDependencies ??
            throw new ArgumentNullException(nameof(meshletDependencies));
    }

    public VoxelWorld World { get; }

    public MeshDependencyStamp Dependencies { get; }

    public IReadOnlyDictionary<ChunkMeshletKey, MeshDependencyStamp>
        MeshletDependencies { get; }
}
