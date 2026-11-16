namespace Asteria.Core.World;

/// <summary>Flags that determine which distance graph consumes a ticket.</summary>
[Flags]
public enum ChunkTicketFlags : byte
{
    None = 0,
    Loading = 1 << 0,
    Simulation = 1 << 1,
    Persist = 1 << 2,
    KeepDimensionActive = 1 << 3,
    // Only UNKNOWN uses this: its countdown runs even if its holder is still loading.
    CanExpireIfUnloaded = 1 << 4,
}

/// <summary>
/// Sources of chunk interest. Flags choose the loading/simulation graphs,
/// persistence and the special UNKNOWN expiry behavior. Other timed tickets
/// pause their countdown until the holder is absent or ready for saving.
/// </summary>
public enum ChunkTicketType : byte
{
    PlayerLoading,
    PlayerSimulation,
    Forced,
    Portal,
    EnderPearl,
    Dragon,
    PlayerSpawn,
    SpawnSearch,
    Unknown,
}

/// <summary>
/// Minecraft-style chunk status thresholds. Lower ticket levels represent
/// stronger interest; loading levels above Full continue through earlier
/// generation statuses without making the chunk accessible to gameplay.
/// </summary>
public static class ChunkLevel
{
    public const int EntityTicking = 31;
    public const int BlockTicking = 32;
    public const int Full = 33;
    public const int InitializeLight = 34;
    public const int Terrain = 35;
    public const int Biomes = 36;
    public const int StructureStarts = 37;

    // Loading and generation are represented by separate projections of
    // the generation DAG. Only HolderStatus requirements extend the holder
    // loading graph; immutable field-read halos never retain adjacent holders.
    public static int MaximumLoading =>
        Full + AsteriaChunkLoadingPlan.MaximumHorizontalDependencyRadius;
    public const int Unloaded = 45;
    public const int NoSimulationTicket = 33;

    public static ChunkFullStatus GetFullStatus(int level)
    {
        if (level <= EntityTicking)
            return ChunkFullStatus.EntityTicking;
        if (level == BlockTicking)
            return ChunkFullStatus.BlockTicking;
        if (level == Full)
            return ChunkFullStatus.Full;
        return ChunkFullStatus.Inaccessible;
    }

    public static ChunkGenerationStatus GetGenerationStatus(int level)
    {
        if (level <= Full)
            return ChunkGenerationStatus.Full;

        var horizontalDistance = level - Full;
        if (horizontalDistance > AsteriaChunkLoadingPlan.MaximumHorizontalDependencyRadius)
            return ChunkGenerationStatus.Unloaded;

        return AsteriaChunkGenerationPlan.GetRequiredStatusAtHorizontalDistance(
            ChunkGenerationStepId.SectionReady,
            horizontalDistance);
    }
}

public enum ChunkFullStatus : byte
{
    Inaccessible,
    Full,
    BlockTicking,
    EntityTicking,
}

public enum ChunkGenerationStatus : byte
{
    Unloaded,
    StructureStarts,
    StructureReferences,
    Biomes,
    Terrain,
    Features,
    InitializeLight,
    Light,
    Spawn,
    Full,
}

/// <summary>
/// A ticket is identified by its type, level and optional Asteria target
/// section within one horizontal chunk column. The X/Z column is still the
/// distance-graph node; SectionY preserves the 3D materialization target
/// which vanilla's full-height 2D chunk does not need.
/// </summary>
public readonly record struct ChunkTicket(
    ChunkTicketType Type,
    int Level,
    int? SectionY = null)
{
    public ChunkTicketFlags Flags => Type switch
    {
        ChunkTicketType.PlayerLoading =>
            ChunkTicketFlags.Loading,
        ChunkTicketType.PlayerSimulation =>
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.Forced =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.Persist |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.Portal =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.Persist |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.EnderPearl =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.Dragon =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation,
        ChunkTicketType.PlayerSpawn or
        ChunkTicketType.SpawnSearch =>
            ChunkTicketFlags.Loading,
        ChunkTicketType.Unknown =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.CanExpireIfUnloaded,
        _ => throw new ArgumentOutOfRangeException(nameof(Type)),
    };

    public ulong? TimeoutTicks => Type switch
    {
        ChunkTicketType.Portal => 300,
        ChunkTicketType.EnderPearl => 40,
        ChunkTicketType.PlayerSpawn => 20,
        ChunkTicketType.SpawnSearch or ChunkTicketType.Unknown => 1,
        _ => null,
    };
}

public readonly record struct ChunkTicketPlacement(
    ChunkColumnCoord Column,
    ChunkTicket Ticket);
