using Asteria.Core.World;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Gameplay;

public enum DimensionTransitionState : byte
{
    Idle = 0,
    Retiring = 1,
}

public sealed record DimensionTransitionCompletion(
    DimensionId From,
    DimensionId To,
    DimensionSessionArchiveReport Archive,
    bool IsCheckpoint = false,
    ulong? SavedGeneration = null,
    string? SaveError = null);

public sealed class DimensionSessionController
{
    private readonly DimensionSessionStateStore _states;
    private readonly Func<
        DimensionSessionState,
        DimensionRuntimeSession> _factory;

    private DimensionId? _target;
    private NVector3? _sourcePosition;
    private NVector3? _destinationPosition;
    private CheckpointRequest? _checkpoint;
    private Task<ulong>? _checkpointPublication;
    private DimensionSessionArchiveReport? _checkpointArchive;

    private sealed record CheckpointRequest(
        string Directory,
        BlockRegistry Blocks,
        FluidRegistry Fluids,
        DyeRegistry Dyes,
        AttachedLayerRegistry Layers);

    public DimensionSessionController(
        DimensionSessionStateStore states,
        Func<
            DimensionSessionState,
            DimensionRuntimeSession> factory)
    {
        _states =
            states ??
            throw new ArgumentNullException(
                nameof(states));
        _factory =
            factory ??
            throw new ArgumentNullException(
                nameof(factory));
    }

    public DimensionRuntimeSession Active { get; private set; } =
        null!;

    public DimensionTransitionState State =>
        _target is null
            ? DimensionTransitionState.Idle
            : DimensionTransitionState.Retiring;

    public bool IsTransitioning =>
        State !=
        DimensionTransitionState.Idle;

    public void Start(
        DimensionId dimension)
    {
        if (Active is not null)
        {
            throw new InvalidOperationException(
                "Dimension session controller is already started.");
        }

        Active =
            _factory(
                _states.GetOrCreate(
                    dimension));
    }

    /// <summary>
    /// Native checkpoint reuses dimension retirement. No live worker or
    /// Godot object may be accessed while Core captures/serializes the
    /// quiescent world on its background publication task.
    /// </summary>
    public bool RequestCheckpoint(
        string directory,
        NVector3 playerPosition,
        BlockRegistry blocks,
        FluidRegistry fluids,
        DyeRegistry dyes,
        AttachedLayerRegistry layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(dyes);
        ArgumentNullException.ThrowIfNull(layers);

        if (Active is null || _target is not null ||
            !float.IsFinite(playerPosition.X) ||
            !float.IsFinite(playerPosition.Y) ||
            !float.IsFinite(playerPosition.Z) || playerPosition.Y < 0)
            return false;

        _checkpoint = new CheckpointRequest(
            directory, blocks, fluids, dyes, layers);
        _sourcePosition = playerPosition;
        _target = Active.Dimension.Id;
        Active.BeginRetirement();
        return true;
    }

    public bool RequestTransition(
        DimensionId target,
        NVector3 sourcePosition,
        NVector3? destinationPosition = null)
    {
        if (Active is null)
        {
            throw new InvalidOperationException(
                "Dimension session controller has not started.");
        }

        if (_target is not null)
        {
            return false;
        }

        if (target ==
            Active.Dimension.Id)
        {
            return false;
        }

        _ = _states.GetOrCreate(
            target);

        _sourcePosition =
            sourcePosition;
        _destinationPosition =
            destinationPosition;
        _target =
            target;
        Active.BeginRetirement();
        return true;
    }

    /// <summary>
    /// Re-enters the current Sphere through the same drain, archive, restore
    /// and loading lifecycle as travel to another Sphere. This is required
    /// for distant warp: moving a player before target residency is ready
    /// would expose nonresident voxels to collision and simulation.
    /// </summary>
    public bool RequestRelocation(
        NVector3 sourcePosition,
        NVector3 destinationPosition)
    {
        if (Active is null)
            throw new InvalidOperationException(
                "Dimension session controller has not started.");

        if (_target is not null ||
            !float.IsFinite(destinationPosition.X) ||
            !float.IsFinite(destinationPosition.Y) ||
            !float.IsFinite(destinationPosition.Z) ||
            destinationPosition.Y < 0)
            return false;

        _sourcePosition = sourcePosition;
        _destinationPosition = destinationPosition;
        _target = Active.Dimension.Id;
        Active.BeginRetirement();
        return true;
    }

    public DimensionTransitionCompletion?
        AdvanceTransition(
            WorldFrameWorkBudget budget,
            Action<DimensionRetirementDrainReport>?
                reportDrain = null)
    {
        if (_target is not
            { } target)
        {
            return null;
        }

        // Publication owns only detached Core state. The active runtime
        // is retired, and no new session/worker may touch its world before
        // this task finishes (even on a failed save).
        if (_checkpointPublication is { } publication)
        {
            if (!publication.IsCompleted)
                return null;

            ulong? generation = null;
            string? error = null;
            try
            {
                generation = publication.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            var retiredArchive = _checkpointArchive ??
                throw new InvalidOperationException("Checkpoint retirement archive is missing.");
            _checkpointPublication = null;
            _checkpointArchive = null;
            _checkpoint = null;
            Active = _factory(_states.GetOrCreate(target));
            _target = null;
            _sourcePosition = null;
            _destinationPosition = null;
            return new DimensionTransitionCompletion(
                target, target, retiredArchive,
                IsCheckpoint: true,
                SavedGeneration: generation,
                SaveError: error);
        }

        var drain =
            Active.DrainRetirement(
                budget);
        reportDrain?.Invoke(
            drain);

        if (!Active.IsQuiescent)
        {
            return null;
        }

        var previous =
            Active.Dimension.Id;
        var archive =
            Active.Retire(
                _sourcePosition);

        if (_checkpoint is { } checkpoint)
        {
            _checkpointArchive = archive;
            // Capture runs only after retirement has finished and before
            // any new session is published. On completion the old Sphere
            // is reconstructed regardless of save success or failure.
            _checkpointPublication = Task.Run(() =>
            {
                var snapshot = GameplaySessionSaveCodec.Capture(
                    _states, checkpoint.Blocks, checkpoint.Fluids,
                    checkpoint.Dyes, checkpoint.Layers,
                    activeSphere: previous);
                var generation = SessionSaveStorage.Publish(
                    checkpoint.Directory, snapshot,
                    checkpoint.Blocks, checkpoint.Fluids,
                    checkpoint.Dyes, checkpoint.Layers);
                WorldSaveManifestPublisher.Publish(
                    checkpoint.Directory, snapshot, generation);
                return generation;
            });
            return null;
        }

        Active =
            _factory(
                _states.GetOrCreate(
                    target));
        if (_destinationPosition is
            { } destination)
        {
            Active.PrepareGeneratedDestination(
                destination);
        }

        _target = null;
        _sourcePosition = null;
        _destinationPosition = null;

        return new DimensionTransitionCompletion(
            previous,
            target,
            archive);
    }
}
