namespace Asteria.Core.World;

/// <summary>
/// Pure chunk materialization boundary consumed by residency workers.
/// Implementations may be invoked concurrently and therefore must be
/// deterministic for the same immutable inputs and safe for concurrent reads.
/// </summary>
public interface IChunkProvider
{
    Chunk Materialize(ChunkCoord coord);
}

/// <summary>
/// Ordered, resumable work units for building one authoritative chunk.
/// The chunk remains private to its job until every stage completes.
/// </summary>
public enum ChunkMaterializationStage : byte
{
    ProviderFallback,
    TerrainSetup,
    TerrainLayer,
    SurfaceDecorations,
    VolumeCavePalette,
    ExteriorFacePalette,
    VolumeStructures,
    GeneratedFluids,
    SurfaceStructures,
    CaveSpikes,
    VerticalDecorations,
    Veins,
    WallLayers,
    WallObjects,
    ShellFinalization,
    StructureObjects,
    PendingObjects,
    Complete,
}

/// <summary>
/// Stateful worker-owned chunk build. Advance performs one bounded stage
/// unit and never publishes partial chunk state to the live world.
/// </summary>
public interface IChunkMaterializationJob : IDisposable
{
    ChunkCoord Coord { get; }

    ChunkMaterializationStage Stage { get; }

    bool IsComplete { get; }

    Chunk? Result { get; }

    void Advance();
}

/// <summary>
/// Optional incremental materialization capability. Providers that do not
/// implement it remain supported through a single-step compatibility adapter.
/// </summary>
public interface IIncrementalChunkProvider : IChunkProvider
{
    IChunkMaterializationJob BeginMaterialization(ChunkCoord coord);
}
