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
    int HorizontalBoundaryColumnCount = 0);

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
public interface IIncrementalChunkProvider : IChunkProvider
{
    IChunkMaterializationJob BeginMaterialization(ChunkCoord coord);
}

/// <summary>
/// Maintains per-voxel directional exposure when chunks arrive or block
/// occupancy changes. Implementations consume materialized occupancy only.
/// </summary>
public interface IChunkDirectionalFacePaletteUpdater
{
    IReadOnlyList<WorldVoxelCoord> ReconcileChunkArrival(
        VoxelWorld world, ChunkCoord arriving);

}
