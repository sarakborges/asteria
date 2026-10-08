using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Pure, bounded safety policy shared by same-Sphere and cross-Sphere warp.
/// World loading/residency remain with their existing authoritative owners.
/// </summary>
public enum WarpArrivalPhase : byte
{
    Requested,
    DestinationFallback,
    Returning,
    ReturnFallback,
}

public enum WarpArrivalDisposition : byte
{
    Enter,
    RetryAtGeneratedSpawn,
    ReturnToOrigin,
    NoSafeEntry,
}

public readonly record struct WarpArrivalPlan(
    DimensionId Origin,
    Vector3 OriginPosition,
    WarpArrivalPhase Phase = WarpArrivalPhase.Requested)
{
    public bool IsReturning =>
        Phase is WarpArrivalPhase.Returning or
            WarpArrivalPhase.ReturnFallback;

    public WarpArrivalDisposition Evaluate(bool residentEntrySafe) =>
        residentEntrySafe
            ? WarpArrivalDisposition.Enter
            : Phase switch
            {
                WarpArrivalPhase.Requested or WarpArrivalPhase.Returning =>
                    WarpArrivalDisposition.RetryAtGeneratedSpawn,
                WarpArrivalPhase.DestinationFallback =>
                    WarpArrivalDisposition.ReturnToOrigin,
                WarpArrivalPhase.ReturnFallback =>
                    WarpArrivalDisposition.NoSafeEntry,
                _ => throw new InvalidOperationException(
                    $"Unsupported warp phase {Phase}."),
            };

    public WarpArrivalPlan RetryAtGeneratedSpawn() =>
        Phase switch
        {
            WarpArrivalPhase.Requested =>
                this with { Phase = WarpArrivalPhase.DestinationFallback },
            WarpArrivalPhase.Returning =>
                this with { Phase = WarpArrivalPhase.ReturnFallback },
            _ => throw new InvalidOperationException(
                $"Cannot retry generated spawn from {Phase}."),
        };

    public WarpArrivalPlan BeginReturn() =>
        Phase == WarpArrivalPhase.DestinationFallback
            ? this with { Phase = WarpArrivalPhase.Returning }
            : throw new InvalidOperationException(
                $"Cannot begin warp rollback from {Phase}.");
}
