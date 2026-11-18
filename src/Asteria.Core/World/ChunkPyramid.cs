using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>The chunk representation associated with a generation status.</summary>
public enum ChunkType : byte
{
    ProtoChunk,
    LevelChunk,
}

/// <summary>Heightmaps guaranteed to have been maintained after a chunk status.</summary>
public enum ChunkHeightmap : byte
{
    OceanFloorWorldgen,
    WorldSurfaceWorldgen,
    OceanFloor,
    WorldSurface,
    MotionBlocking,
    MotionBlockingNoLeaves,
}

/// <summary>
/// The ten ordered chunk lifecycle statuses from Minecraft Java Edition 26.3.
/// Statuses carry identity, order, parent, chunk representation and heightmaps;
/// scheduling dependencies and work ownership belong to ChunkStep/ChunkPyramid.
/// </summary>
public sealed class ChunkStatus
{
    private static readonly ImmutableArray<ChunkHeightmap> WorldgenHeightmaps =
    [
        ChunkHeightmap.OceanFloorWorldgen,
        ChunkHeightmap.WorldSurfaceWorldgen,
    ];

    private static readonly ImmutableArray<ChunkHeightmap> FinalHeightmaps =
    [
        ChunkHeightmap.OceanFloor,
        ChunkHeightmap.WorldSurface,
        ChunkHeightmap.MotionBlocking,
        ChunkHeightmap.MotionBlockingNoLeaves,
    ];

    public static readonly ChunkStatus Empty =
        new(0, "empty", parent: null, ChunkType.ProtoChunk, WorldgenHeightmaps);
    public static readonly ChunkStatus StructureStarts =
        new(1, "structure_starts", Empty, ChunkType.ProtoChunk, WorldgenHeightmaps);
    public static readonly ChunkStatus StructureReferences =
        new(2, "structure_references", StructureStarts, ChunkType.ProtoChunk, WorldgenHeightmaps);
    public static readonly ChunkStatus Biomes =
        new(3, "biomes", StructureReferences, ChunkType.ProtoChunk, WorldgenHeightmaps);
    public static readonly ChunkStatus Terrain =
        new(4, "terrain", Biomes, ChunkType.ProtoChunk, FinalHeightmaps);
    public static readonly ChunkStatus Features =
        new(5, "features", Terrain, ChunkType.ProtoChunk, FinalHeightmaps);
    public static readonly ChunkStatus InitializeLight =
        new(6, "initialize_light", Features, ChunkType.ProtoChunk, FinalHeightmaps);
    public static readonly ChunkStatus Light =
        new(7, "light", InitializeLight, ChunkType.ProtoChunk, FinalHeightmaps);
    public static readonly ChunkStatus Spawn =
        new(8, "spawn", Light, ChunkType.ProtoChunk, FinalHeightmaps);
    public static readonly ChunkStatus Full =
        new(9, "full", Spawn, ChunkType.LevelChunk, FinalHeightmaps);

    private static readonly ImmutableDictionary<string, ChunkStatus> ByName =
        All.ToImmutableDictionary(
            static status => status.Name,
            StringComparer.Ordinal);

    private ChunkStatus(
        int index,
        string name,
        ChunkStatus? parent,
        ChunkType chunkType,
        ImmutableArray<ChunkHeightmap> heightmapsAfter)
    {
        Index = index;
        Name = name;
        Parent = parent;
        ChunkType = chunkType;
        HeightmapsAfter = heightmapsAfter;
    }

    public static ImmutableArray<ChunkStatus> All { get; } =
    [
        Empty,
        StructureStarts,
        StructureReferences,
        Biomes,
        Terrain,
        Features,
        InitializeLight,
        Light,
        Spawn,
        Full,
    ];

    public int Index { get; }

    public string Name { get; }

    public ChunkStatus? Parent { get; }

    public ChunkType ChunkType { get; }

    public ImmutableArray<ChunkHeightmap> HeightmapsAfter { get; }

    public bool IsAfter(ChunkStatus other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Index > other.Index;
    }

    public bool IsOrAfter(ChunkStatus other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Index >= other.Index;
    }

    public static ChunkStatus Max(ChunkStatus first, ChunkStatus second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.Index >= second.Index ? first : second;
    }

    public static ChunkStatus GetByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ByName.TryGetValue(name, out var status)
            ? status
            : throw new KeyNotFoundException($"Unknown chunk status: {name}");
    }

    public override string ToString() => Name;
}

/// <summary>
/// One status requirement applied to every chunk in a Chebyshev radius around
/// the chunk whose step is being scheduled.
/// </summary>
public readonly record struct ChunkStepRequirement
{
    public ChunkStepRequirement(ChunkStatus status, int radius)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));

        Status = status;
        Radius = radius;
    }

    public ChunkStatus Status { get; }

    public int Radius { get; }
}

/// <summary>
/// The minimum required status at each Chebyshev distance from a target chunk.
/// Entry zero describes the target chunk itself; the last entry defines Radius.
/// </summary>
public sealed class ChunkDependencies
{
    private readonly ImmutableArray<ChunkStatus> _requiredStatusesByDistance;

    internal ChunkDependencies(IEnumerable<ChunkStatus> requiredStatusesByDistance)
    {
        ArgumentNullException.ThrowIfNull(requiredStatusesByDistance);
        _requiredStatusesByDistance = requiredStatusesByDistance.ToImmutableArray();
        if (_requiredStatusesByDistance.Any(static status => status is null))
            throw new ArgumentException(
                "Chunk dependency entries cannot be null.",
                nameof(requiredStatusesByDistance));
    }

    public static ChunkDependencies Empty { get; } =
        new(ImmutableArray<ChunkStatus>.Empty);

    public ImmutableArray<ChunkStatus> RequiredStatusesByDistance =>
        _requiredStatusesByDistance;

    public int Radius => _requiredStatusesByDistance.Length - 1;

    public ChunkStatus? GetRequiredStatus(int distance)
    {
        if (distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance));

        return distance < _requiredStatusesByDistance.Length
            ? _requiredStatusesByDistance[distance]
            : null;
    }

    /// <summary>
    /// Returns the farthest ring whose required status also satisfies the
    /// supplied minimum status. This is how a step determines how far its
    /// predecessor is required before composing the predecessor's dependencies.
    /// </summary>
    public int GetRadiusOf(ChunkStatus minimumStatus)
    {
        ArgumentNullException.ThrowIfNull(minimumStatus);
        var radius = -1;
        for (var distance = 0; distance < _requiredStatusesByDistance.Length; distance++)
        {
            if (_requiredStatusesByDistance[distance].IsOrAfter(minimumStatus))
                radius = distance;
        }

        return radius;
    }

    internal ChunkDependencies MergeShifted(
        ChunkDependencies other,
        int distanceOffset)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (distanceOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceOffset));
        if (other._requiredStatusesByDistance.IsEmpty)
            return this;

        var merged = _requiredStatusesByDistance.ToBuilder();
        for (var index = 0; index < other._requiredStatusesByDistance.Length; index++)
        {
            var targetIndex = checked(index + distanceOffset);
            var requiredStatus = other._requiredStatusesByDistance[index];
            while (merged.Count <= targetIndex)
                merged.Add(requiredStatus);

            merged[targetIndex] = ChunkStatus.Max(
                merged[targetIndex],
                requiredStatus);
        }

        return new ChunkDependencies(merged);
    }
}

/// <summary>
/// Immutable dependency and write-boundary definition for one chunk status.
/// The generation and loading pyramids supply separate ChunkStep instances for
/// the same status because their work and prerequisites are not interchangeable.
/// </summary>
public sealed class ChunkStep
{
    internal ChunkStep(
        ChunkStatus targetStatus,
        ChunkStep? parentStep,
        int blockStateWriteRadius,
        IEnumerable<ChunkStepRequirement> additionalRequirements)
    {
        ArgumentNullException.ThrowIfNull(targetStatus);
        ArgumentNullException.ThrowIfNull(additionalRequirements);
        if (blockStateWriteRadius < -1)
            throw new ArgumentOutOfRangeException(nameof(blockStateWriteRadius));

        if (targetStatus.Parent is null)
        {
            if (parentStep is not null)
                throw new ArgumentException(
                    "The root status cannot have a parent step.",
                    nameof(parentStep));
        }
        else if (parentStep is null ||
                 parentStep.TargetStatus != targetStatus.Parent)
        {
            throw new ArgumentException(
                $"The parent step for {targetStatus} must be {targetStatus.Parent}.",
                nameof(parentStep));
        }

        TargetStatus = targetStatus;
        BlockStateWriteRadius = blockStateWriteRadius;
        DirectDependencies = BuildDirectDependencies(
            targetStatus.Parent,
            additionalRequirements);

        if (parentStep is null)
        {
            AccumulatedDependencies = DirectDependencies;
        }
        else
        {
            var parentRadius = DirectDependencies.GetRadiusOf(parentStep.TargetStatus);
            if (parentRadius < 0)
                throw new InvalidOperationException(
                    $"The direct dependencies of {targetStatus} omit its parent status.");

            AccumulatedDependencies = DirectDependencies.MergeShifted(
                parentStep.AccumulatedDependencies,
                parentRadius);
        }
    }

    public ChunkStatus TargetStatus { get; }

    public ChunkDependencies DirectDependencies { get; }

    public ChunkDependencies AccumulatedDependencies { get; }

    /// <summary>
    /// Chebyshev radius within which this step may write block states through
    /// its generation region. -1 means that region writes are prohibited.
    /// </summary>
    public int BlockStateWriteRadius { get; }

    private static ChunkDependencies BuildDirectDependencies(
        ChunkStatus? parentStatus,
        IEnumerable<ChunkStepRequirement> additionalRequirements)
    {
        var dependencies = new List<ChunkStatus>();
        if (parentStatus is not null)
            dependencies.Add(parentStatus);

        foreach (var requirement in additionalRequirements)
        {
            if (requirement.Status is null)
                throw new ArgumentException(
                    "A chunk step requirement must specify a status.",
                    nameof(additionalRequirements));

            while (dependencies.Count <= requirement.Radius)
                dependencies.Add(requirement.Status);

            for (var distance = 0; distance <= requirement.Radius; distance++)
            {
                dependencies[distance] = ChunkStatus.Max(
                    dependencies[distance],
                    requirement.Status);
            }
        }

        return new ChunkDependencies(dependencies);
    }
}

/// <summary>
/// The two status-to-step plans used by Minecraft Java Edition 26.3:
/// one for generating missing chunks and one for loading persisted chunks.
/// </summary>
public sealed class ChunkPyramid
{
    private ChunkPyramid(IEnumerable<ChunkStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Steps = steps.ToImmutableArray();
        if (Steps.Length != ChunkStatus.All.Length)
            throw new ArgumentException(
                "A chunk pyramid must define exactly one step for every chunk status.",
                nameof(steps));

        for (var index = 0; index < Steps.Length; index++)
        {
            if (Steps[index].TargetStatus != ChunkStatus.All[index])
                throw new ArgumentException(
                    $"Chunk pyramid step {index} must target {ChunkStatus.All[index]}.",
                    nameof(steps));
        }
    }

    public static ChunkPyramid Generation { get; } = CreateGeneration();

    public static ChunkPyramid Loading { get; } = CreateLoading();

    public ImmutableArray<ChunkStep> Steps { get; }

    public ChunkStep FullStep => GetStep(ChunkStatus.Full);

    public ChunkStep GetStep(ChunkStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if ((uint)status.Index >= (uint)Steps.Length ||
            Steps[status.Index].TargetStatus != status)
            throw new ArgumentException(
                "The status does not belong to this chunk pyramid.",
                nameof(status));

        return Steps[status.Index];
    }

    private static ChunkPyramid CreateGeneration()
    {
        var steps = new List<ChunkStep>();

        Add(ChunkStatus.Empty);
        Add(ChunkStatus.StructureStarts);
        Add(
            ChunkStatus.StructureReferences,
            [new ChunkStepRequirement(ChunkStatus.StructureStarts, 8)]);
        Add(
            ChunkStatus.Biomes,
            [new ChunkStepRequirement(ChunkStatus.StructureStarts, 8)]);
        Add(
            ChunkStatus.Terrain,
            [
                new ChunkStepRequirement(ChunkStatus.StructureStarts, 8),
                new ChunkStepRequirement(ChunkStatus.Biomes, 1),
            ],
            blockStateWriteRadius: 0);
        Add(
            ChunkStatus.Features,
            [
                new ChunkStepRequirement(ChunkStatus.StructureStarts, 8),
                new ChunkStepRequirement(ChunkStatus.Terrain, 1),
            ],
            blockStateWriteRadius: 1);
        Add(ChunkStatus.InitializeLight);
        Add(
            ChunkStatus.Light,
            [new ChunkStepRequirement(ChunkStatus.InitializeLight, 1)]);
        Add(
            ChunkStatus.Spawn,
            [new ChunkStepRequirement(ChunkStatus.Biomes, 1)]);
        Add(ChunkStatus.Full);

        return new ChunkPyramid(steps);

        void Add(
            ChunkStatus status,
            ChunkStepRequirement[]? requirements = null,
            int blockStateWriteRadius = -1)
        {
            var parent = steps.Count == 0 ? null : steps[^1];
            steps.Add(new ChunkStep(
                status,
                parent,
                blockStateWriteRadius,
                requirements ?? []));
        }
    }

    private static ChunkPyramid CreateLoading()
    {
        var steps = new List<ChunkStep>();

        Add(ChunkStatus.Empty);
        Add(ChunkStatus.StructureStarts);
        Add(ChunkStatus.StructureReferences);
        Add(ChunkStatus.Biomes);
        Add(ChunkStatus.Terrain);
        Add(ChunkStatus.Features);
        Add(ChunkStatus.InitializeLight);
        Add(
            ChunkStatus.Light,
            [new ChunkStepRequirement(ChunkStatus.InitializeLight, 1)]);
        Add(ChunkStatus.Spawn);
        Add(ChunkStatus.Full);

        return new ChunkPyramid(steps);

        void Add(
            ChunkStatus status,
            ChunkStepRequirement[]? requirements = null,
            int blockStateWriteRadius = -1)
        {
            var parent = steps.Count == 0 ? null : steps[^1];
            steps.Add(new ChunkStep(
                status,
                parent,
                blockStateWriteRadius,
                requirements ?? []));
        }
    }
}
