namespace Asteria.Core.World;

public enum WorldLoadingPhase : byte
{
    Inactive = 0,
    RetiringCurrentDimension = 1,
    MaterializingInitialArea = 2,
    PreparingPresentation = 3,
    Ready = 4,
    PreparingArrivalStructure = 5,
}

public readonly record struct WorldLoadingProgress(
    WorldLoadingPhase Phase,
    int Completed,
    int Total)
{
    public bool IsReady =>
        Phase ==
        WorldLoadingPhase.Ready;
}

/// <summary>
/// Readiness requirements for the initial bootstrap presentation.
/// Ongoing world simulation is intentionally not a readiness dependency:
/// it may continue after gameplay begins while revisions protect publications.
/// </summary>
public readonly record struct WorldLoadingReadiness(
    bool ResidencySettled,
    bool SelectionSettled,
    bool LightingSettled,
    bool TerrainMeshWorkSettled,
    bool TerrainPublicationsSettled,
    bool FluidMeshWorkSettled,
    bool FluidPublicationsSettled,
    bool ObjectPresentationSettled)
{
    public bool IsSettled =>
        ResidencySettled &&
        SelectionSettled &&
        LightingSettled &&
        TerrainMeshWorkSettled &&
        TerrainPublicationsSettled &&
        FluidMeshWorkSettled &&
        FluidPublicationsSettled &&
        ObjectPresentationSettled;
}

/// <summary>
/// Authoritative runtime loading state. It reports only work owned by the
/// existing streaming/residency/presentation pipeline; it owns no timers and
/// never invents progress.
/// </summary>
public sealed class WorldLoadingState
{
    private WorldLoadingProgress _progress =
        new(
            WorldLoadingPhase.Inactive,
            0,
            0);

    public bool IsActive =>
        _progress.Phase is
            WorldLoadingPhase.RetiringCurrentDimension or
            WorldLoadingPhase.MaterializingInitialArea or
            WorldLoadingPhase.PreparingPresentation or
            WorldLoadingPhase.PreparingArrivalStructure;

    // Seam reconciliation is derived from chunk residency. During the initial
    // bootstrap, let the selected chunks finish materializing before dispatching
    // that work so snapshots do not repeatedly go stale as neighbors arrive.
    public bool AllowsLightingDispatch =>
        _progress.Phase is not (
            WorldLoadingPhase.RetiringCurrentDimension or
            WorldLoadingPhase.MaterializingInitialArea);

    public ChunkCoord Center { get; private set; }

    public WorldLoadingProgress Progress =>
        _progress;

    public void BeginRetirement()
    {
        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.RetiringCurrentDimension,
                0,
                0);
    }

    public void Begin(
        ChunkCoord center)
    {
        Center = center;
        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.MaterializingInitialArea,
                0,
                0);
    }

    public void UpdateResidency(
        int desired,
        int pending,
        int materializing,
        bool selectionRunning)
    {
        if (_progress.Phase !=
            WorldLoadingPhase.MaterializingInitialArea)
        {
            return;
        }

        if (desired < 0 ||
            pending < 0 ||
            materializing < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desired));
        }

        var completed =
            Math.Clamp(
                desired -
                pending -
                materializing,
                0,
                desired);

        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.MaterializingInitialArea,
                completed,
                desired);

        if (desired > 0 &&
            completed ==
                desired &&
            pending == 0 &&
            materializing == 0 &&
            !selectionRunning)
        {
            _progress =
                new WorldLoadingProgress(
                    WorldLoadingPhase.PreparingPresentation,
                    0,
                    1);
        }
    }

    /// <summary>
    /// Gameplay can start only after every bootstrap chunk is presented and
    /// all presentation-critical work has settled. Streaming simulation such
    /// as fluid evolution is independent and continues after world entry.
    /// Counts describe actual current presentation, not estimated progress.
    /// </summary>
    public bool UpdatePresentation(
        int fullyPublishedChunks,
        int desiredChunks,
        WorldLoadingReadiness readiness)
    {
        if (_progress.Phase !=
            WorldLoadingPhase.PreparingPresentation)
        {
            return false;
        }

        if (desiredChunks <= 0 ||
            fullyPublishedChunks < 0 ||
            fullyPublishedChunks > desiredChunks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fullyPublishedChunks));
        }

        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.PreparingPresentation,
                fullyPublishedChunks,
                desiredChunks);

        if (fullyPublishedChunks != desiredChunks || !readiness.IsSettled)
            return false;

        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.Ready,
                desiredChunks,
                desiredChunks);
        return true;
    }

    /// <summary>Additional authored voxel work has been committed after
    /// the initial area became renderable; its collision must be republished.
    /// </summary>
    public void BeginArrivalStructurePresentation()
    {
        if (_progress.Phase != WorldLoadingPhase.Ready)
            throw new InvalidOperationException(
                "Arrival Structure preparation requires a ready initial area.");
        _progress = new WorldLoadingProgress(
            WorldLoadingPhase.PreparingArrivalStructure, 0, 1);
    }

    public void UpdateArrivalStructurePresentation(bool presentationCurrent)
    {
        if (_progress.Phase != WorldLoadingPhase.PreparingArrivalStructure)
            return;
        _progress = presentationCurrent
            ? new WorldLoadingProgress(WorldLoadingPhase.Ready, 1, 1)
            : new WorldLoadingProgress(
                WorldLoadingPhase.PreparingArrivalStructure, 0, 1);
    }

    public void Reset()
    {
        Center = default;
        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.Inactive,
                0,
                0);
    }
}
