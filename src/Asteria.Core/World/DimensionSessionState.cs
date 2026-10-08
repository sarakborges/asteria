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
        WorldGameRules gameRules)
    {
        Dimension =
            dimension ??
            throw new ArgumentNullException(
                nameof(dimension));
        DimensionSeed =
            dimensionSeed;
        GameRules = gameRules ??
            throw new ArgumentNullException(nameof(gameRules));
        World =
            new VoxelWorld();
    }

    public DimensionDefinition Dimension { get; }

    public ulong DimensionSeed { get; }

    public WorldGameRules GameRules { get; }

    public VoxelWorld World { get; }

    public ulong WorldTick { get; set; }

    public DayNightClockState? DayNight { get; set; }

    public Vector3? PlayerPosition { get; set; }

    public BlockPhysicsRuntimeSnapshot?
        BlockPhysics { get; set; }

    public DroppedBlockRuntimeSnapshot?
        DroppedBlocks { get; set; }

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
        InitialGameMode = creation.GameMode;
        GameRules = new WorldGameRules(
            creation.TicksPerSecond,
            creation.SpawnCreatures);
    }

    public string Name { get; }

    public PlayerGameMode InitialGameMode { get; }

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
                GameRules);
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
