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
    DimensionSessionArchiveReport Archive);

public sealed class DimensionSessionController
{
    private readonly DimensionSessionStateStore _states;
    private readonly Func<
        DimensionSessionState,
        DimensionRuntimeSession> _factory;

    private DimensionId? _target;
    private NVector3? _sourcePosition;
    private NVector3? _destinationPosition;

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
