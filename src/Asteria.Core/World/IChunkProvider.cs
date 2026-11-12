namespace Asteria.Core.World;

/// <summary>
/// Diagnostic counters and sampled timings for one directional face-palette
/// pass. Counts are collected in every build; substage timings are sampled only
/// in Debug builds.
/// </summary>
public readonly record struct DirectionalFacePaletteMetrics(
    bool HasTimingBreakdown,
    int TimingSampleInterval,
    int SurfaceColumns,
    int SolidVoxels,
    int FaceProbes,
    int NeighborColumnLookups,
    int SolidPaletteLookups,
    int EmptyRegionPaletteLookups,
    int DensityProbes,
    int BoundaryDensityProbes,
    int EmptyRegionOwnerResolutions,
    int MaskRejectedProbes,
    int EmptyRegionEligibilityRejectedProbes,
    int PaletteRejectedProbes,
    int DepthRejectedProbes,
    int PaletteWrites,
    double TotalMilliseconds,
    double SurfaceColumnPreparationEstimatedMilliseconds,
    double SolidOwnerResolutionEstimatedMilliseconds,
    double NeighborOwnerLookupEstimatedMilliseconds,
    double DensityProbeEstimatedMilliseconds,
    double EmptyRegionOwnerResolutionEstimatedMilliseconds,
    double PaletteWriteEstimatedMilliseconds,
    double OtherEstimatedMilliseconds,
    double DensitySlabReadMilliseconds = 0d,
    double HorizontalBoundaryReadMilliseconds = 0d,
    double OccupancyRowAssemblyMilliseconds = 0d,
    double CandidateMaskBuildMilliseconds = 0d,
    int HorizontalBoundaryBatchCount = 0,
    int HorizontalBoundaryColumnCount = 0,
    double DensityTerrainColumnMilliseconds = 0d,
    double OccupancyCacheAccessMilliseconds = 0d,
    double SurfaceBiomeSamplingMilliseconds = 0d,
    double BaseHeightSamplingMilliseconds = 0d,
    double VolumeBiomeSamplingMilliseconds = 0d);

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
    DirectionalFacePalettes,
    VolumeStructures,
    GeneratedFluids,
    SurfaceStructures,
    CaveSpikes,
    VerticalDecorations,
    Veins,
    WallLayers,
    WallObjects,
    ShellFinalization,
    GravitySupport,
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

    /// <summary>
    /// Coordinate of the leaf work unit advanced by the next Advance call.
    /// Composite jobs expose their active child for accurate stage diagnostics.
    /// </summary>
    ChunkCoord ActiveWorkCoord => Coord;

    /// <summary>
    /// Stage of the leaf work unit advanced by the next Advance call.
    /// </summary>
    ChunkMaterializationStage ActiveWorkStage => Stage;

    bool IsComplete { get; }

    Chunk? Result { get; }

    /// <summary>Optional profile for the completed directional-palette stage.</summary>
    DirectionalFacePaletteMetrics? DirectionalFacePaletteMetrics => null;
    /// <summary>Debug timing for discovering missing horizontal boundary columns.</summary>
    double DirectionalFaceBoundaryColumnBoundsMilliseconds => 0d;

    /// <summary>Debug timing for constructing the cave-noise sampler for boundary columns.</summary>
    double DirectionalFaceCaveSamplerBuildMilliseconds => 0d;

    /// <summary>Debug timing for evaluating missing horizontal boundary occupancy.</summary>
    double DirectionalFaceBoundaryColumnEvaluationMilliseconds => 0d;

    void Advance();
}

/// <summary>
/// Optional incremental materialization capability. Providers that do not
/// implement it remain supported through a single-step compatibility adapter.
/// </summary>
internal readonly record struct PaletteStorageGrowthDelta(
    long ArrayPayloadBytes,
    int GrowthEvents);

internal readonly record struct ChunkMaterializationStorageDiagnostics(
    bool Available,
    PaletteStorageGrowthDelta TerrainLayerBlockPalette,
    PaletteStorageGrowthDelta TerrainLayerDirectionalFacePalette,
    PaletteStorageGrowthDelta DirectionalFaceBlockPalette,
    PaletteStorageGrowthDelta DirectionalFaceMetadataPalette,
    WorldGenerationEntropy.ValueNoise3DLatticeCacheMetrics TerrainLayerGeologyNoiseCache);

internal interface IChunkMaterializationStorageDiagnostics
{
    ChunkMaterializationStorageDiagnostics StorageDiagnostics { get; }
}

public interface IIncrementalChunkProvider : IChunkProvider
{
    IChunkMaterializationJob BeginMaterialization(ChunkCoord coord);
}

/// <summary>
/// Immutable dependency envelope plus isolated chunk copies for one arrival
/// reconciliation. The live world is never read by the worker.
/// </summary>
public sealed class DirectionalFacePaletteReconciliationSnapshot
{
    internal DirectionalFacePaletteReconciliationSnapshot(
        ChunkCoord arriving,
        VoxelWorld workerWorld,
        MeshDependencyStamp dependencies)
    {
        Arriving = arriving;
        WorkerWorld = workerWorld ?? throw new ArgumentNullException(nameof(workerWorld));
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public ChunkCoord Arriving { get; }

    internal VoxelWorld WorkerWorld { get; }

    public MeshDependencyStamp Dependencies { get; }
}

/// <summary>Worker-computed directional palette changes awaiting validated publication.</summary>
public sealed class DirectionalFacePaletteReconciliationResult
{
    internal DirectionalFacePaletteReconciliationResult(
        ChunkCoord arriving,
        VoxelWorld workerWorld,
        MeshDependencyStamp dependencies,
        IReadOnlyList<ChunkCoord> changedChunks,
        IReadOnlyList<WorldVoxelCoord> visualChanges)
    {
        Arriving = arriving;
        WorkerWorld = workerWorld ?? throw new ArgumentNullException(nameof(workerWorld));
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        ChangedChunks = changedChunks ?? throw new ArgumentNullException(nameof(changedChunks));
        VisualChanges = visualChanges ?? throw new ArgumentNullException(nameof(visualChanges));
    }

    public ChunkCoord Arriving { get; }

    internal VoxelWorld WorkerWorld { get; }

    public MeshDependencyStamp Dependencies { get; }

    public IReadOnlyList<ChunkCoord> ChangedChunks { get; }

    public IReadOnlyList<WorldVoxelCoord> VisualChanges { get; }
}

/// <summary>
/// Maintains per-voxel directional exposure when chunks arrive or block
/// occupancy changes. Calculation runs against isolated snapshots and is
/// committed only while every captured dependency remains current.
/// </summary>
public interface IChunkDirectionalFacePaletteUpdater
{
    IReadOnlyList<WorldVoxelCoord> ReconcileChunkArrival(
        VoxelWorld world, ChunkCoord arriving);

    DirectionalFacePaletteReconciliationSnapshot CaptureChunkArrivalSnapshot(
        VoxelWorld world, ChunkCoord arriving);

    DirectionalFacePaletteReconciliationResult ReconcileChunkArrival(
        DirectionalFacePaletteReconciliationSnapshot snapshot);

    bool TryApplyChunkArrivalReconciliation(
        VoxelWorld world,
        DirectionalFacePaletteReconciliationResult result,
        out IReadOnlyList<WorldVoxelCoord> visualChanges);
}
