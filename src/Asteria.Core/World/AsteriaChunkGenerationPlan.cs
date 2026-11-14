namespace Asteria.Core.World;

/// <summary>
/// The authoritative stage DAG for Asteria's chunk materialization pipeline.
/// Read envelopes constrain procedural/immutable data access; only dependencies
/// explicitly marked HolderStatus contribute to ticket loading and holder claims.
/// </summary>
public static class AsteriaChunkGenerationPlan
{
    private static readonly ChunkGenerationDependencyEnvelope NoHalo =
        ChunkGenerationDependencyEnvelope.None;

    private static readonly ChunkGenerationDependencyEnvelope HorizontalPaletteHalo =
        new(horizontalRadius: 1, minimumSectionOffset: -1, maximumSectionOffset: 1);

    private static readonly ChunkGenerationDependencyEnvelope LowerSupportHalo =
        new(horizontalRadius: 0, minimumSectionOffset: -1, maximumSectionOffset: -1);

    public static ChunkGenerationPlan Default { get; } = new(
    [
        Step(
            ChunkGenerationStepId.ColumnBiomeData,
            ChunkGenerationWorkScope.Column,
            ChunkGenerationExecutionPolicy.SharedWorkerPool),

        Step(
            ChunkGenerationStepId.SurfaceTerrainColumn,
            ChunkGenerationWorkScope.Column,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.ColumnBiomeData)),

        Step(
            ChunkGenerationStepId.SectionTerrainSetup,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.SurfaceTerrainColumn)),

        Step(
            ChunkGenerationStepId.SectionTerrainLayers,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.SectionTerrainSetup)),

        Step(
            ChunkGenerationStepId.SurfaceDecorations,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.SectionTerrainLayers)),

        // Directional face palettes sample procedural surface/terrain data in
        // cardinal columns and across the vertical voxel halo. This is a data
        // prerequisite, not a requirement that those neighbouring chunks be
        // fully materialized or made resident.
        Step(
            ChunkGenerationStepId.DirectionalFacePalettes,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(
                    ChunkGenerationStepId.SurfaceDecorations,
                    NoHalo),
                Dependency(
                    ChunkGenerationStepId.SurfaceTerrainColumn,
                    HorizontalPaletteHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ],
            readEnvelope: HorizontalPaletteHalo),

        Step(
            ChunkGenerationStepId.VolumeStructures,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.DirectionalFacePalettes, NoHalo),
                Dependency(ChunkGenerationStepId.ColumnBiomeData, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.GeneratedFluids,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.VolumeStructures)),

        Step(
            ChunkGenerationStepId.SurfaceStructures,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.VolumeStructures, NoHalo),
                Dependency(ChunkGenerationStepId.ColumnBiomeData, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.CaveSpikes,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.SurfaceStructures, NoHalo),
                Dependency(ChunkGenerationStepId.SectionTerrainLayers, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.VerticalDecorations,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.CaveSpikes)),

        Step(
            ChunkGenerationStepId.Veins,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.VerticalDecorations)),

        Step(
            ChunkGenerationStepId.WallLayers,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.Veins)),

        Step(
            ChunkGenerationStepId.WallObjects,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.WallLayers)),

        Step(
            ChunkGenerationStepId.ShellFinalization,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.WallObjects)),

        // Gravity support can cross the lower section boundary. Deeper
        // gravity chains are represented as runtime dependencies created as
        // each support query discovers another unsupported gravity voxel.
        Step(
            ChunkGenerationStepId.GravitySupport,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.ShellFinalization, NoHalo),
                Dependency(
                    ChunkGenerationStepId.SectionTerrainLayers,
                    LowerSupportHalo),
            ],
            readEnvelope: LowerSupportHalo),

        Step(
            ChunkGenerationStepId.StructureObjects,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.GravitySupport, NoHalo),
                Dependency(ChunkGenerationStepId.SurfaceStructures, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.PendingObjects,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            DependsOn(ChunkGenerationStepId.StructureObjects)),

        Step(
            ChunkGenerationStepId.InitialLighting,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.LightExecutor,
            DependsOn(ChunkGenerationStepId.PendingObjects)),

        Step(
            ChunkGenerationStepId.SectionReady,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.MainThreadIntegration,
            DependsOn(ChunkGenerationStepId.InitialLighting)),
    ]);

    /// <summary>
    /// Resolves the materializer's concrete state to the authoritative
    /// generation-plan step. The fallback provider path has no decomposed
    /// stage graph and is treated as one detached worker operation.
    /// </summary>
    public static ChunkGenerationStepId GetStepId(
        ChunkMaterializationStage stage) =>
        stage switch
        {
            ChunkMaterializationStage.TerrainSetup =>
                ChunkGenerationStepId.SectionTerrainSetup,
            ChunkMaterializationStage.TerrainLayer =>
                ChunkGenerationStepId.SectionTerrainLayers,
            ChunkMaterializationStage.SurfaceDecorations =>
                ChunkGenerationStepId.SurfaceDecorations,
            ChunkMaterializationStage.DirectionalFacePalettes =>
                ChunkGenerationStepId.DirectionalFacePalettes,
            ChunkMaterializationStage.VolumeStructures =>
                ChunkGenerationStepId.VolumeStructures,
            ChunkMaterializationStage.GeneratedFluids =>
                ChunkGenerationStepId.GeneratedFluids,
            ChunkMaterializationStage.SurfaceStructures =>
                ChunkGenerationStepId.SurfaceStructures,
            ChunkMaterializationStage.CaveSpikes =>
                ChunkGenerationStepId.CaveSpikes,
            ChunkMaterializationStage.VerticalDecorations =>
                ChunkGenerationStepId.VerticalDecorations,
            ChunkMaterializationStage.Veins =>
                ChunkGenerationStepId.Veins,
            ChunkMaterializationStage.WallLayers =>
                ChunkGenerationStepId.WallLayers,
            ChunkMaterializationStage.WallObjects =>
                ChunkGenerationStepId.WallObjects,
            ChunkMaterializationStage.ShellFinalization =>
                ChunkGenerationStepId.ShellFinalization,
            ChunkMaterializationStage.GravitySupport =>
                ChunkGenerationStepId.GravitySupport,
            ChunkMaterializationStage.StructureObjects =>
                ChunkGenerationStepId.StructureObjects,
            ChunkMaterializationStage.PendingObjects =>
                ChunkGenerationStepId.PendingObjects,
            ChunkMaterializationStage.ProviderFallback or
            ChunkMaterializationStage.Complete =>
                throw new ArgumentOutOfRangeException(
                    nameof(stage),
                    stage,
                    "This materialization state does not identify a generation-plan step."),
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };

    /// <summary>
    /// Returns the runtime execution lane declared by the authoritative
    /// generation plan for one concrete materialization state.
    /// </summary>
    public static ChunkGenerationExecutionPolicy GetExecutionPolicy(
        ChunkMaterializationStage stage) =>
        stage switch
        {
            ChunkMaterializationStage.ProviderFallback =>
                ChunkGenerationExecutionPolicy.SharedWorkerPool,
            ChunkMaterializationStage.Complete =>
                throw new ArgumentOutOfRangeException(nameof(stage)),
            _ => Default.GetStep(GetStepId(stage)).ExecutionPolicy,
        };

    public static ChunkGenerationDependencyEnvelope FullSectionEnvelope =>
        Default.GetHolderDependencyEnvelope(ChunkGenerationStepId.SectionReady);

    /// <summary>
    /// Complete spatial access envelope, including immutable generator-field
    /// reads that do not require adjacent chunk holders.
    /// </summary>
    public static ChunkGenerationDependencyEnvelope FullSectionAccessEnvelope =>
        Default.GetDependencyEnvelope(ChunkGenerationStepId.SectionReady);

    public static int MaximumHorizontalDependencyRadius =>
        FullSectionEnvelope.HorizontalRadius;

    /// <summary>
    /// Returns the deepest generation status required at a horizontal
    /// Chebyshev distance from a target step. Requirements come from the
    /// target's transitive prerequisite envelopes, rather than from a copied
    /// vanilla level-to-status table.
    /// </summary>
    public static ChunkGenerationStatus GetRequiredStatusAtHorizontalDistance(
        ChunkGenerationStepId targetStep,
        int horizontalDistance)
    {
        if (horizontalDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalDistance));

        var requiredStatus = ChunkGenerationStatus.Unloaded;
        foreach (var dependency in Default.GetAccumulatedDependencies(targetStep))
        {
            if (dependency.Kind != ChunkGenerationDependencyKind.HolderStatus ||
                dependency.Envelope.HorizontalRadius < horizontalDistance)
                continue;

            var dependencyStatus = GetGenerationStatus(
                dependency.PrerequisiteStep);
            if (dependencyStatus > requiredStatus)
                requiredStatus = dependencyStatus;
        }

        return requiredStatus;
    }

    /// <summary>
    /// Maps Asteria's concrete materialization DAG onto the holder status
    /// ladder. Ticket levels decide the highest status a holder may reach;
    /// a requested step maps to the future that represents its actual target.
    /// </summary>
    public static ChunkGenerationStatus GetGenerationStatus(
        ChunkGenerationStepId step) =>
        step switch
        {
            ChunkGenerationStepId.ColumnBiomeData =>
                ChunkGenerationStatus.Biomes,
            ChunkGenerationStepId.SurfaceTerrainColumn or
            ChunkGenerationStepId.SectionTerrainSetup or
            ChunkGenerationStepId.SectionTerrainLayers =>
                ChunkGenerationStatus.Terrain,
            ChunkGenerationStepId.SurfaceDecorations or
            ChunkGenerationStepId.DirectionalFacePalettes or
            ChunkGenerationStepId.VolumeStructures or
            ChunkGenerationStepId.GeneratedFluids or
            ChunkGenerationStepId.SurfaceStructures or
            ChunkGenerationStepId.CaveSpikes or
            ChunkGenerationStepId.VerticalDecorations or
            ChunkGenerationStepId.Veins or
            ChunkGenerationStepId.WallLayers or
            ChunkGenerationStepId.WallObjects or
            ChunkGenerationStepId.ShellFinalization or
            ChunkGenerationStepId.GravitySupport or
            ChunkGenerationStepId.StructureObjects or
            ChunkGenerationStepId.PendingObjects =>
                ChunkGenerationStatus.Features,
            ChunkGenerationStepId.InitialLighting =>
                ChunkGenerationStatus.Light,
            ChunkGenerationStepId.SectionReady =>
                ChunkGenerationStatus.Full,
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

    private static ChunkGenerationStepDefinition Step(
        ChunkGenerationStepId id,
        ChunkGenerationWorkScope scope,
        ChunkGenerationExecutionPolicy policy,
        IEnumerable<ChunkGenerationStepDependency>? dependencies = null,
        ChunkGenerationDependencyEnvelope? readEnvelope = null,
        ChunkGenerationDependencyEnvelope? writeEnvelope = null) =>
        new(
            id,
            scope,
            policy,
            dependencies ?? Array.Empty<ChunkGenerationStepDependency>(),
            readEnvelope ?? NoHalo,
            writeEnvelope ?? NoHalo);

    private static ChunkGenerationStepDependency[] DependsOn(
        ChunkGenerationStepId prerequisite) =>
        [Dependency(prerequisite, NoHalo)];

    private static ChunkGenerationStepDependency Dependency(
        ChunkGenerationStepId prerequisite,
        ChunkGenerationDependencyEnvelope envelope,
        ChunkGenerationDependencyKind kind =
            ChunkGenerationDependencyKind.HolderStatus) =>
        new(prerequisite, envelope, kind);
}
