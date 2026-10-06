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

    public bool IsCurrent(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.IsContentStampCurrent(Content) &&
               world.IsResidencyStampCurrent(Residency);
    }
}

public sealed class MeshWorkerSnapshot
{
    internal MeshWorkerSnapshot(
        VoxelWorld world,
        MeshDependencyStamp dependencies)
    {
        World =
            world ??
            throw new ArgumentNullException(nameof(world));
        Dependencies =
            dependencies ??
            throw new ArgumentNullException(nameof(dependencies));
    }

    public VoxelWorld World { get; }

    public MeshDependencyStamp Dependencies { get; }
}
