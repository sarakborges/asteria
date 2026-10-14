namespace Asteria.Core.World;

[Flags]
public enum LightingDependencyChanges : byte
{
    None = 0,
    Content = 1,
    Residency = 2,
}

public sealed class VoxelLightingDependencies
{
    private readonly ChunkContentStamp _content;
    private readonly ChunkColumnResidencyStamp _residency;

    internal VoxelLightingDependencies(
        ChunkContentStamp content,
        ChunkColumnResidencyStamp residency)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _residency = residency ?? throw new ArgumentNullException(nameof(residency));
    }

    /// <summary>
    /// Classify stale lighting work for diagnostics. Both dependencies are
    /// inspected even if one is invalid, so combined causes stay observable.
    /// Never used to bypass stale-result rejection.
    /// </summary>
    public LightingDependencyChanges DetectChanges(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var changes = LightingDependencyChanges.None;
        if (!world.IsContentStampCurrent(_content))
            changes |= LightingDependencyChanges.Content;
        if (!world.IsColumnResidencyStampCurrent(_residency))
            changes |= LightingDependencyChanges.Residency;
        return changes;
    }

    public bool IsCurrent(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.IsContentStampCurrent(_content) &&
               world.IsColumnResidencyStampCurrent(_residency);
    }
}

public sealed class VoxelLightingSnapshot
{
    private const int HorizontalChunkRadius = 1;

    private VoxelLightingSnapshot(
        VoxelWorld world,
        VoxelLightingDependencies dependencies)
    {
        World = world;
        Dependencies = dependencies;
    }

    public VoxelWorld World { get; }

    public VoxelLightingDependencies Dependencies { get; }

    public static VoxelLightingSnapshot Capture(
        VoxelWorld source,
        IEnumerable<WorldVoxelCoord> seeds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(seeds);

        var seedColumns = seeds
            .Select(position => VoxelCoordinates.FromWorld(
                position.X,
                Math.Max(position.Y, 0),
                position.Z).Chunk)
            .Select(ChunkColumnCoord.FromChunk)
            .Distinct()
            .ToArray();

        if (seedColumns.Length == 0)
        {
            throw new ArgumentException(
                "A lighting snapshot requires at least one seed position.",
                nameof(seeds));
        }

        var dependencyColumns = new HashSet<ChunkColumnCoord>();
        foreach (var center in seedColumns)
        {
            for (var z = -HorizontalChunkRadius; z <= HorizontalChunkRadius; z++)
            {
                for (var x = -HorizontalChunkRadius; x <= HorizontalChunkRadius; x++)
                {
                    dependencyColumns.Add(
                        new ChunkColumnCoord(center.X + x, center.Z + z));
                }
            }
        }

        var chunks = source.LoadedChunkCoords
            .Where(coord => dependencyColumns.Contains(
                ChunkColumnCoord.FromChunk(coord)))
            .OrderBy(coord => coord.Y)
            .ThenBy(coord => coord.Z)
            .ThenBy(coord => coord.X)
            .ToArray();

        return new VoxelLightingSnapshot(
            source.CloneForWorker(chunks),
            new VoxelLightingDependencies(
                source.CaptureContentStamp(chunks),
                source.CaptureColumnResidencyStamp(dependencyColumns)));
    }
}
