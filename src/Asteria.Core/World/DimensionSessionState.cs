using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct DimensionSessionArchiveReport(
    int ArchivedDirty,
    int ArchivedPristine)
{
    public int Total =>
        ArchivedDirty +
        ArchivedPristine;
}

public sealed class DimensionSessionState
{
    public DimensionSessionState(
        DimensionDefinition dimension,
        ulong dimensionSeed,
        WorldGameRules gameRules,
        WorldGenerationOptions? generation = null)
        : this(dimension, dimensionSeed, gameRules, generation, null)
    {
    }

    internal DimensionSessionState(
        DimensionDefinition dimension,
        ulong dimensionSeed,
        WorldGameRules gameRules,
        WorldGenerationOptions? generation,
        VoxelWorld? restoredWorld)
    {
        Dimension =
            dimension ??
            throw new ArgumentNullException(
                nameof(dimension));
        DimensionSeed =
            dimensionSeed;
        GameRules = gameRules ??
            throw new ArgumentNullException(nameof(gameRules));
        Generation = generation ?? new WorldGenerationOptions();
        World =
            restoredWorld ?? new VoxelWorld();
    }

    public DimensionDefinition Dimension { get; }

    public ulong DimensionSeed { get; }

    public WorldGameRules GameRules { get; }

    public WorldGenerationOptions Generation { get; }

    public VoxelWorld World { get; }

    /// <summary>Committed manual structures remain Sphere-isolated across
    /// archive/retirement. Like World, the owner survives session recreation.</summary>
    public ManualStructurePlacementLedger ManualStructures { get; } = new();

    /// <summary>Storage-box contents remain isolated by Sphere and survive
    /// unload/retirement together with the Sphere's authoritative VoxelWorld.</summary>
    public StorageBoxRuntime StorageBoxes { get; } = new();

    public ulong WorldTick { get; set; }

    public DayNightClockState? DayNight { get; set; }

    public Vector3? PlayerPosition { get; set; }

    public BlockPhysicsRuntimeSnapshot?
        BlockPhysics { get; set; }

    public FluidUpdateQueueSnapshot? PendingFluidWork { get; set; }

    public BlockPhysicsUpdateQueueSnapshot? PendingPhysicsWork { get; set; }

    public DroppedBlockRuntimeSnapshot?
        DroppedBlocks { get; set; }

    public CreatureRuntimeSnapshot? Creatures { get; set; }

    public ulong NaturalSpawnNextAttemptTick { get; set; }

    public DimensionSessionArchiveReport
        ArchiveResidentWorld()
    {
        var archivedDirty = 0;
        var archivedPristine = 0;

        foreach (var result in
                 World.ArchiveAllResidentChunks())
        {
            switch (result.Result)
            {
                case ChunkArchiveResult.ArchivedDirty:
                    archivedDirty++;
                    break;
                case ChunkArchiveResult.ArchivedPristine:
                    archivedPristine++;
                    break;
            }
        }

        return new DimensionSessionArchiveReport(
            archivedDirty,
            archivedPristine);
    }
}

public sealed class DimensionSessionStateStore
{
    private readonly ulong _worldSeed;
    private readonly DimensionRegistry _dimensions;
    private readonly Dictionary<
        DimensionId,
        DimensionSessionState> _states = [];

    public DimensionSessionStateStore(
        ulong worldSeed,
        DimensionRegistry dimensions)
        : this(
            new WorldCreationOptions(
                WorldCreationOptions.DefaultName,
                worldSeed),
            dimensions)
    {
    }

    public DimensionSessionStateStore(
        WorldCreationOptions creation,
        DimensionRegistry dimensions)
    {
        ArgumentNullException.ThrowIfNull(creation);
        _worldSeed = creation.Seed;
        _dimensions = dimensions ??
            throw new ArgumentNullException(nameof(dimensions));
        Name = creation.Name;
        Generation = creation.Generation;
        Player = new PlayerSessionState(creation.GameMode);
        GameRules = new WorldGameRules(
            creation.TicksPerSecond,
            creation.SpawnCreatures);
    }

    internal ulong WorldSeed => _worldSeed;

    internal IReadOnlyList<DimensionSessionState> CreatedDimensions() =>
        _states.Values.OrderBy(
            state => state.Dimension.Id.Value, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Restores one validated world into a new store before any session
    /// starts. A loaded Sphere never shares chunks with the source world.
    /// </summary>
    internal void AddRestoredDimension(DimensionId dimensionId, VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_states.ContainsKey(dimensionId) || _states.Count >= _dimensions.Count)
            throw new InvalidOperationException(
                $"Sphere {dimensionId} is already initialized or exceeds the registry.");
        var definition = _dimensions.Get(dimensionId);
        _states.Add(dimensionId, new DimensionSessionState(
            definition, DimensionSeed.Derive(_worldSeed, definition.Id),
            GameRules, Generation.ForSphere(definition), world));
    }

    public string Name { get; }

    public WorldGenerationOptions Generation { get; }

    public PlayerSessionState Player { get; }

    public WorldGameRules GameRules { get; }

    public int Count =>
        _states.Count;

    public DimensionSessionState GetOrCreate(
        DimensionId dimensionId)
    {
        if (_states.TryGetValue(
                dimensionId,
                out var state))
        {
            return state;
        }

        if (_states.Count >=
            _dimensions.Count)
        {
            throw new InvalidOperationException(
                "Dimension session state count exceeded authored dimension count.");
        }

        var definition =
            _dimensions.Get(
                dimensionId);
        state =
            new DimensionSessionState(
                definition,
                DimensionSeed.Derive(
                    _worldSeed,
                    definition.Id),
                GameRules,
                Generation.ForSphere(definition));
        _states.Add(
            dimensionId,
            state);
        return state;
    }

    public bool TryGet(
        DimensionId dimensionId,
        out DimensionSessionState state) =>
        _states.TryGetValue(
            dimensionId,
            out state!);
}
