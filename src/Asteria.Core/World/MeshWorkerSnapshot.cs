namespace Asteria.Core.World;

public static class MeshWorkerSnapshotLimits
{
    // Bound synchronous main-thread capture by keeping each worker batch local
    // to a small number of source chunks and their dependency neighborhoods.
    public const int MaximumSourceChunksPerBatch = 4;
}

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
        // These stamps describe collision geometry, not task lifetime.
        // Epoch changes on a lossless archive/restore do not alter voxels.
        return Content.HasSameSpatialEntriesAs(other.Content) &&
               Residency.HasSameCoordinatesAs(other.Residency);
    }

    public bool IsCurrent(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Workers remain strict: even when archived content has not changed,
        // an archive/restore that occurred while the job ran invalidates its
        // captured residency epochs. Only published GPU meshes can use the
        // relaxed content-only comparison.
        return world.IsResidencyStampCurrent(Residency) &&
               HasUnchangedGeometryAfterRestore(world);
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
        // A retired neighbor remains materialized in the archive. Check
        // actual spatial content rather than whether it is currently active.
        return Content.MatchesMaterializedFootprint(world, Residency);
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
