namespace Asteria.Core.World;

/// <summary>
/// The authoritative stage DAG for Asteria's chunk materialization pipeline.
/// Spatial dependency envelopes describe the source data a stage may read;
/// they are also used to derive the ticket graph's loading radius.
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
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.SectionTerrainLayers)),

        // Directional face palettes sample procedural surface/terrain data in
        // cardinal columns and across the vertical voxel halo. This is a data
        // prerequisite, not a requirement that those neighbouring chunks be
        // fully materialized or made resident.
        Step(
            ChunkGenerationStepId.DirectionalFacePalettes,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            [
                Dependency(
                    ChunkGenerationStepId.SurfaceDecorations,
                    NoHalo),
                Dependency(
                    ChunkGenerationStepId.SurfaceTerrainColumn,
                    HorizontalPaletteHalo),
            ],
            readEnvelope: HorizontalPaletteHalo),

        Step(
            ChunkGenerationStepId.VolumeStructures,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            [
                Dependency(ChunkGenerationStepId.DirectionalFacePalettes, NoHalo),
                Dependency(ChunkGenerationStepId.ColumnBiomeData, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.GeneratedFluids,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.VolumeStructures)),

        Step(
            ChunkGenerationStepId.SurfaceStructures,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            [
                Dependency(ChunkGenerationStepId.VolumeStructures, NoHalo),
                Dependency(ChunkGenerationStepId.ColumnBiomeData, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.CaveSpikes,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            [
                Dependency(ChunkGenerationStepId.SurfaceStructures, NoHalo),
                Dependency(ChunkGenerationStepId.SectionTerrainLayers, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.VerticalDecorations,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.CaveSpikes)),

        Step(
            ChunkGenerationStepId.Veins,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.VerticalDecorations)),

        Step(
            ChunkGenerationStepId.WallLayers,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.Veins)),

        Step(
            ChunkGenerationStepId.WallObjects,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.WallLayers)),

        Step(
            ChunkGenerationStepId.ShellFinalization,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.WallObjects)),

        // Gravity support can cross the lower section boundary. Deeper
        // gravity chains are represented as runtime dependencies created as
        // each support query discovers another unsupported gravity voxel.
        Step(
            ChunkGenerationStepId.GravitySupport,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
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
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            [
                Dependency(ChunkGenerationStepId.GravitySupport, NoHalo),
                Dependency(ChunkGenerationStepId.SurfaceStructures, NoHalo),
            ]),

        Step(
            ChunkGenerationStepId.PendingObjects,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.StructureObjects)),

        Step(
            ChunkGenerationStepId.InitialLighting,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.PendingObjects)),

        Step(
            ChunkGenerationStepId.SectionReady,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.MainThreadIntegration,
            DependsOn(ChunkGenerationStepId.InitialLighting)),
    ]);

    public static ChunkGenerationDependencyEnvelope FullSectionEnvelope =>
        Default.GetDependencyEnvelope(ChunkGenerationStepId.SectionReady);

    public static int MaximumHorizontalDependencyRadius =>
        FullSectionEnvelope.HorizontalRadius;

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
        ChunkGenerationDependencyEnvelope envelope) =>
        new(prerequisite, envelope);
}
