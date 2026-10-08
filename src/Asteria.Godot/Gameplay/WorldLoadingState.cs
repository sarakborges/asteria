using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

public enum WorldLoadingPhase : byte
{
    Inactive = 0,
    RetiringCurrentDimension = 1,
    MaterializingInitialArea = 2,
    PreparingPresentation = 3,
    Ready = 4,
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
/// Authoritative runtime loading state. It reports only work owned by the
/// existing streaming/residency/presentation pipeline; it owns no timers and
/// never invents progress.
/// </summary>
public sealed class WorldLoadingState
{
    // Bootstrap the spawn chunk and its immediate neighbors. The full
    // configured render distance streams after the spawn is presented.
    public const int InitialHorizontalRadiusChunks = 1;

    private WorldLoadingProgress _progress =
        new(
            WorldLoadingPhase.Inactive,
            0,
            0);

    public bool IsActive =>
        _progress.Phase is
            WorldLoadingPhase.RetiringCurrentDimension or
            WorldLoadingPhase.MaterializingInitialArea or
            WorldLoadingPhase.PreparingPresentation;

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

    public bool UpdatePresentation(
        bool destinationPresentationReady)
    {
        if (_progress.Phase !=
            WorldLoadingPhase.PreparingPresentation)
        {
            return false;
        }

        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.PreparingPresentation,
                destinationPresentationReady
                    ? 1
                    : 0,
                1);

        if (!destinationPresentationReady)
        {
            return false;
        }

        _progress =
            new WorldLoadingProgress(
                WorldLoadingPhase.Ready,
                1,
                1);
        return true;
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
