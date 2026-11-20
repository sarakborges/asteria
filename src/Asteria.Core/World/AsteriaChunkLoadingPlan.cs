using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>
/// Asteria's loading/retention pyramid, kept separate from the generation DAG.
///
/// The generation plan describes every prerequisite, including immutable
/// procedural-field reads. The loading plan projects only HolderStatus
/// prerequisites into exact section futures and horizontal holder leases.
/// ImmutableWorldData halos remain guarded by ChunkGenerationAccessRegion and
/// never create resident chunk holders.
/// </summary>
public static class AsteriaChunkLoadingPlan
{
    private static readonly ChunkGenerationPlan Generation =
        AsteriaChunkGenerationPlan.Default;

    /// <summary>
    /// Holder-retention envelope required to make a section FULL. This is the
    /// Asteria loading pyramid's maximum dependency footprint, not the larger
    /// immutable field-read envelope used by world generation.
    /// </summary>
    public static ChunkGenerationDependencyEnvelope FullSectionDependencyEnvelope =>
        GetHolderDependencyEnvelope(ChunkGenerationStepId.SectionReady);

    /// <summary>Largest horizontal holder radius in the full-section loading plan.</summary>
    public static int MaximumHorizontalDependencyRadius =>
        FullSectionDependencyEnvelope.HorizontalRadius;

    /// <summary>
    /// Returns the spatial envelope whose holder identities must be retained
    /// while the target step runs. Immutable generator data is excluded.
    /// </summary>
    public static ChunkGenerationDependencyEnvelope GetHolderDependencyEnvelope(
        ChunkGenerationStepId targetStep) =>
        Generation.GetHolderDependencyEnvelope(targetStep);

    /// <summary>
    /// Expands the loading plan's HolderStatus prerequisites into exact 3D
    /// section/status requirements. ImmutableWorldData reads are deliberately
    /// absent; they are supplied by generator-owned immutable field snapshots.
    /// </summary>
    public static ImmutableArray<ChunkGenerationSectionRequirement>
        GetHolderStatusRequirements(
            ChunkGenerationStepId targetStep,
            ChunkCoord targetSection) =>
        AsteriaChunkGenerationPlan.GetHolderStatusRequirements(
            targetStep, targetSection);
}
