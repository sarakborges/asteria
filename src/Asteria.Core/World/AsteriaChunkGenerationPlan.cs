using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>One exact holder-status prerequisite derived from the generation DAG.</summary>
public readonly record struct ChunkGenerationSectionRequirement(
    ChunkCoord Coord,
    ChunkGenerationStepId PrerequisiteStep,
    ChunkGenerationStatus Status);

/// <summary>
/// The authoritative stage DAG for Asteria's chunk materialization pipeline.
/// Read envelopes constrain procedural/immutable data access; only dependencies
/// explicitly marked HolderStatus contribute to ticket loading and holder claims.
/// </summary>
public static class AsteriaChunkGenerationPlan
{
    private static readonly ChunkGenerationDependencyEnvelope NoHalo =
        ChunkGenerationDependencyEnvelope.None;

    private static readonly ChunkGenerationDependencyEnvelope HorizontalNeighborHalo =
        new(horizontalRadius: 1, minimumSectionOffset: 0, maximumSectionOffset: 0);

    private static readonly ChunkGenerationDependencyEnvelope HorizontalDecorationReadHalo =
        new(horizontalRadius: 2, minimumSectionOffset: 0, maximumSectionOffset: 0);

    private static readonly ChunkGenerationDependencyEnvelope HorizontalPaletteHalo =
        new(horizontalRadius: 1, minimumSectionOffset: -1, maximumSectionOffset: 1);

    // Volume-structure anchor selection scans the authored support range
    // (at most 512 blocks) plus templates extending at most 16 blocks from
    // their support face. Horizontal templates are bounded to radius six.
    private static readonly ChunkGenerationDependencyEnvelope VolumeStructureReadHalo =
        new(horizontalRadius: 1, minimumSectionOffset: -34, maximumSectionOffset: 34);

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
            [
                Dependency(
                    ChunkGenerationStepId.ColumnBiomeData,
                    NoHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ]),

        Step(
            ChunkGenerationStepId.SectionTerrainSetup,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            [
                Dependency(
                    ChunkGenerationStepId.SurfaceTerrainColumn,
                    NoHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ]),

        Step(
            ChunkGenerationStepId.SectionTerrainLayers,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            DependsOn(ChunkGenerationStepId.SectionTerrainSetup)),

        // Surface decorators sample cardinal slope columns and nearby
        // fluids. The authored swamp cattail rule reaches two columns, but all
        // such probes are immutable column data rather than holder-status reads.
        Step(
            ChunkGenerationStepId.SurfaceDecorations,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.SectionTerrainLayers, NoHalo),
                Dependency(
                    ChunkGenerationStepId.SurfaceTerrainColumn,
                    HorizontalDecorationReadHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ],
            readEnvelope: HorizontalDecorationReadHalo),

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

        // Anchor selection reads immutable biome/density fields through the
        // authored vertical support range. It does not require distant holders.
        Step(
            ChunkGenerationStepId.VolumeStructures,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.DirectionalFacePalettes, NoHalo),
                Dependency(
                    ChunkGenerationStepId.ColumnBiomeData,
                    VolumeStructureReadHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ],
            readEnvelope: VolumeStructureReadHalo),

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
            [
                Dependency(ChunkGenerationStepId.Veins, NoHalo),
                Dependency(
                    ChunkGenerationStepId.SurfaceTerrainColumn,
                    HorizontalNeighborHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ],
            readEnvelope: HorizontalNeighborHalo),

        Step(
            ChunkGenerationStepId.WallObjects,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.OrderedMutation,
            [
                Dependency(ChunkGenerationStepId.WallLayers, NoHalo),
                Dependency(
                    ChunkGenerationStepId.SurfaceTerrainColumn,
                    HorizontalNeighborHalo,
                    ChunkGenerationDependencyKind.ImmutableWorldData),
            ],
            readEnvelope: HorizontalNeighborHalo),

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
    /// Expands a target step's accumulated HolderStatus dependencies into
    /// exact 3D section/status requirements. Immutable generator-data reads
    /// are excluded because they do not require a resident holder.
    /// </summary>
    public static ImmutableArray<ChunkGenerationSectionRequirement>
        GetHolderStatusRequirements(
            ChunkGenerationStepId targetStep,
            ChunkCoord targetSection)
    {
        if (targetSection.Y < 0)
            throw new ArgumentOutOfRangeException(
                nameof(targetSection), "Sphere chunk Y cannot be negative.");

        var requirements = new Dictionary<
            (ChunkCoord Coord, ChunkGenerationStepId Step),
            ChunkGenerationStatus>();

        foreach (var dependency in Default.GetAccumulatedDependencies(targetStep))
        {
            if (dependency.Kind != ChunkGenerationDependencyKind.HolderStatus)
                continue;

            var status = GetGenerationStatus(dependency.PrerequisiteStep);
            for (var sectionOffset = dependency.Envelope.MinimumSectionOffset;
                 sectionOffset <= dependency.Envelope.MaximumSectionOffset;
                 sectionOffset++)
            {
                var sectionY = (long)targetSection.Y + sectionOffset;
                if (sectionY < 0 || sectionY > int.MaxValue)
                    continue;

                for (var dz = -dependency.Envelope.HorizontalRadius;
                     dz <= dependency.Envelope.HorizontalRadius;
                     dz++)
                for (var dx = -dependency.Envelope.HorizontalRadius;
                     dx <= dependency.Envelope.HorizontalRadius;
                     dx++)
                {
                    var x = (long)targetSection.X + dx;
                    var z = (long)targetSection.Z + dz;
                    if (x < int.MinValue || x > int.MaxValue ||
                        z < int.MinValue || z > int.MaxValue)
                        continue;

                    var coord = new ChunkCoord(
                        (int)x, (int)sectionY, (int)z);
                    var key = (coord, dependency.PrerequisiteStep);
                    if (!requirements.TryGetValue(key, out var existing) ||
                        status > existing)
                        requirements[key] = status;
                }
            }
        }

        return requirements
            .Select(entry => new ChunkGenerationSectionRequirement(
                entry.Key.Coord, entry.Key.Step, entry.Value))
            .OrderBy(item => item.Coord.Y)
            .ThenBy(item => item.Coord.Z)
            .ThenBy(item => item.Coord.X)
            .ThenBy(item => item.PrerequisiteStep)
            .ToImmutableArray();
    }

    /// <summary>
    /// Checks whether a concrete section product satisfies a holder-status
    /// prerequisite declared by the transitive DAG for the target step.
    /// Immutable generator-data reads are intentionally not accepted here.
    /// </summary>
    public static bool SatisfiesHolderStatusRequirement(
        ChunkGenerationStepId targetStep,
        ChunkCoord targetSection,
        ChunkCoord dependencySection,
        ChunkGenerationStatus availableStatus)
    {
        if (targetSection.Y < 0 || dependencySection.Y < 0 ||
            !Enum.IsDefined(availableStatus) ||
            availableStatus == ChunkGenerationStatus.Unloaded)
            return false;

        var deltaX = (long)dependencySection.X - targetSection.X;
        var deltaY = (long)dependencySection.Y - targetSection.Y;
        var deltaZ = (long)dependencySection.Z - targetSection.Z;
        var requiredStatus = ChunkGenerationStatus.Unloaded;
        foreach (var dependency in Default.GetAccumulatedDependencies(targetStep))
        {
            if (dependency.Kind != ChunkGenerationDependencyKind.HolderStatus ||
                Math.Max(Math.Abs(deltaX), Math.Abs(deltaZ)) >
                    dependency.Envelope.HorizontalRadius ||
                deltaY < dependency.Envelope.MinimumSectionOffset ||
                deltaY > dependency.Envelope.MaximumSectionOffset)
                continue;

            var stepStatus = GetGenerationStatus(dependency.PrerequisiteStep);
            if (stepStatus > requiredStatus)
                requiredStatus = stepStatus;
        }

        return requiredStatus != ChunkGenerationStatus.Unloaded &&
            availableStatus >= requiredStatus;
    }

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
