using System.Numerics;
using Asteria.Client.Rendering;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Gameplay;

public sealed record DimensionRuntimeSessionSettings(
    uint WorldTicksPerSecond,
    int RenderDistanceChunks,
    int RetentionMarginChunks,
    int MaxMaterializationTasksInFlight,
    int MaxMaterializationDispatchesPerFrame,
    int MaxMaterializationResultsPerFrame,
    int MaxPresentationPublicationsPerFrame,
    int MaxInteractiveTerrainMeshletsPerWorker,
    int MaxTerrainMeshletsPerWorker,
    int MaxFluidMeshletsPerWorker,
    int MaxFluidUpdatesPerWorker,
    int MaxChunkEvictionsPerFrame);

public sealed record DimensionRetirementDrainReport(
    ChunkResidencyUpdate Materialization,
    IReadOnlyList<Exception> WorkerErrors);

public sealed class DimensionRuntimeSession
{
    private const int SpawnSearchRadiusBlocks = 64;
    private const int SpawnBiomeSearchRadiusBlocks = 512;

    private readonly DimensionSessionState _state;
    private NVector3 _initialPlayerPosition;
    private readonly BlockPhysicsRuntime _blockPhysics;
    private readonly DroppedBlockRuntime _droppedBlocks;
    private bool _retiring;
    private bool _retired;

    public DimensionRuntimeSession(
        Node3D parent,
        DimensionSessionState state,
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeRegistry biomes,
        StructureRegistry structures,
        StructureSetRegistry structureSets,
        TerrainTextureLookup terrainTextures,
        VoxelTerrainMaterialSet terrainMaterials,
        FluidMaterialCatalog fluidMaterials,
        DimensionRuntimeSessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            parent);
        _state =
            state ??
            throw new ArgumentNullException(
                nameof(state));
        Blocks =
            blocks ??
            throw new ArgumentNullException(
                nameof(blocks));
        Fluids =
            fluids ??
            throw new ArgumentNullException(
                nameof(fluids));
        ArgumentNullException.ThrowIfNull(
            biomes);
        ArgumentNullException.ThrowIfNull(
            structures);
        ArgumentNullException.ThrowIfNull(
            structureSets);
        ArgumentNullException.ThrowIfNull(
            terrainTextures);
        ArgumentNullException.ThrowIfNull(
            terrainMaterials);
        ArgumentNullException.ThrowIfNull(
            fluidMaterials);
        Settings =
            settings ??
            throw new ArgumentNullException(
                nameof(settings));

        Root =
            new Node3D
            {
                Name =
                    $"DimensionSession_{state.Dimension.Id.Value.Replace(':', '_')}",
            };
        parent.AddChild(
            Root);

        World =
            state.World;
        WorldUpdates =
            new WorldUpdateQueue();
        WorldTicks =
            new WorldTickClock(
                state.WorldTick);
        FluidUpdates =
            new FluidUpdateQueue();
        FluidMeshUpdates =
            new FluidMeshUpdateQueue();
        BlockPhysicsUpdates =
            new BlockPhysicsUpdateQueue();
        ContentRevisions =
            new MeshletContentRevisions();
        FluidContentRevisions =
            new MeshletContentRevisions();
        Generator =
            new BiomeWorldGenerator(
                state.DimensionSeed,
                state.Dimension,
                blocks,
                fluids,
                biomes,
                structures,
                structureSets);
        _initialPlayerPosition =
            state.PlayerPosition ??
            ResolveGeneratedSpawn();
        _droppedBlocks =
            new DroppedBlockRuntime(
                World,
                blocks,
                restore:
                    state.DroppedBlocks);
        Mutations =
            new VoxelMutationRuntime(
                World,
                WorldUpdates,
                FluidUpdates,
                FluidMeshUpdates,
                BlockPhysicsUpdates,
                ContentRevisions,
                FluidContentRevisions);
        var lightingIntegration =
            new LightingResultIntegrator(
                World,
                WorldUpdates,
                FluidMeshUpdates);
        FluidSimulation =
            new FluidSimulationRuntime(
                World,
                fluids,
                FluidUpdates,
                Mutations,
                WorldTicks,
                settings.WorldTicksPerSecond,
                settings.MaxFluidUpdatesPerWorker);
        Lighting =
            new LightingRuntime(
                World,
                blocks,
                fluids,
                WorldUpdates,
                lightingIntegration);
        _blockPhysics =
            new BlockPhysicsRuntime(
                World,
                blocks,
                Mutations,
                BlockPhysicsUpdates,
                _droppedBlocks,
                restore:
                    state.BlockPhysics);
        BlockInteractions =
            new BlockInteractionRuntime(
                World,
                blocks,
                Mutations,
                _droppedBlocks);

        var blockEntityPresentations =
            new BlockEntityPresentationController(
                Root,
                blocks,
                fluids,
                terrainTextures,
                terrainMaterials);
        BlockEntities =
            new BlockEntityFrameController(
                WorldTicks,
                _blockPhysics,
                _droppedBlocks,
                BlockPhysicsUpdates,
                blockEntityPresentations);

        Residency =
            new ChunkResidencyRuntime(
                World,
                blocks,
                fluids,
                Generator,
                WorldUpdates,
                FluidUpdates,
                FluidMeshUpdates,
                _blockPhysics,
                ContentRevisions,
                FluidContentRevisions,
                WorldTicks,
                new ChunkResidencySettings(
                    settings.MaxMaterializationTasksInFlight,
                    settings.MaxMaterializationDispatchesPerFrame,
                    settings.MaxMaterializationResultsPerFrame,
                    settings.MaxChunkEvictionsPerFrame,
                    settings.WorldTicksPerSecond));
        Presentations =
            new ChunkPresentationController(
                Root,
                World,
                WorldUpdates,
                FluidMeshUpdates,
                ContentRevisions,
                FluidContentRevisions,
                terrainMaterials,
                fluidMaterials);
        TerrainMesh =
            new TerrainMeshPipeline(
                World,
                blocks,
                terrainTextures,
                Generator.Tints,
                WorldUpdates,
                ContentRevisions,
                Presentations,
                settings.MaxInteractiveTerrainMeshletsPerWorker,
                settings.MaxTerrainMeshletsPerWorker);
        FluidMesh =
            new FluidMeshPipeline(
                World,
                blocks,
                fluids,
                FluidMeshUpdates,
                FluidContentRevisions,
                Presentations,
                settings.MaxFluidMeshletsPerWorker);
        Streaming =
            new ChunkStreamingController(
                Residency,
                Presentations,
                Generator,
                new ChunkStreamingControllerSettings(
                    settings.RenderDistanceChunks,
                    settings.RetentionMarginChunks,
                    settings.MaxPresentationPublicationsPerFrame));
    }

    public DimensionRuntimeSessionSettings Settings { get; }

    public DimensionDefinition Dimension =>
        _state.Dimension;

    public ulong DimensionSeed =>
        _state.DimensionSeed;

    public Node3D Root { get; }

    public BlockRegistry Blocks { get; }

    public FluidRegistry Fluids { get; }

    public VoxelWorld World { get; }

    public WorldUpdateQueue WorldUpdates { get; }

    public WorldTickClock WorldTicks { get; }

    public FluidUpdateQueue FluidUpdates { get; }

    public FluidMeshUpdateQueue FluidMeshUpdates { get; }

    public BlockPhysicsUpdateQueue BlockPhysicsUpdates { get; }

    public MeshletContentRevisions ContentRevisions { get; }

    public MeshletContentRevisions FluidContentRevisions { get; }

    public BiomeWorldGenerator Generator { get; }

    public VoxelMutationRuntime Mutations { get; }

    public BlockInteractionRuntime BlockInteractions { get; }

    public BlockEntityFrameController BlockEntities { get; }

    public ChunkResidencyRuntime Residency { get; }

    public ChunkPresentationController Presentations { get; }

    public ChunkStreamingController Streaming { get; }

    public TerrainMeshPipeline TerrainMesh { get; }

    public FluidMeshPipeline FluidMesh { get; }

    public FluidSimulationRuntime FluidSimulation { get; }

    public LightingRuntime Lighting { get; }

    public bool IsRetiring =>
        _retiring;

    public bool IsQuiescent =>
        Residency.MaterializingCount == 0 &&
        !Streaming.IsSelectionRunning &&
        !FluidSimulation.IsRunning &&
        !Lighting.IsRunning &&
        !TerrainMesh.IsRunning &&
        !FluidMesh.IsRunning;

    public NVector3 InitialPlayerPosition =>
        _initialPlayerPosition;

    private NVector3 ResolveGeneratedSpawn()
    {
        var spawn =
            Dimension.Spawn;
        var destination =
            Generator.FindGeneratedSpawn(
                SpawnBiomeSearchRadiusBlocks,
                SpawnSearchRadiusBlocks) ??
            throw new InvalidOperationException(
                $"Dimension {Dimension.Id} has no safe generated spawn near ({spawn.X}, {spawn.Z}).");

        return new NVector3(
            destination.X + 0.5f,
            destination.Y,
            destination.Z + 0.5f);
    }

    public void PrepareGeneratedDestination(
        NVector3 preferred)
    {
        var preferredX =
            checked(
                (int)MathF.Floor(
                    preferred.X));
        var preferredY =
            checked(
                (int)MathF.Floor(
                    preferred.Y));
        var preferredZ =
            checked(
                (int)MathF.Floor(
                    preferred.Z));
        var destination =
            Generator.FindGeneratedDestination(
                preferredX,
                preferredY,
                preferredZ,
                SpawnSearchRadiusBlocks) ??
            throw new InvalidOperationException(
                $"Dimension {Dimension.Id} has no safe generated destination within {SpawnSearchRadiusBlocks} blocks of ({preferredX}, {preferredY}, {preferredZ}).");

        _initialPlayerPosition =
            new NVector3(
                destination.X + 0.5f,
                destination.Y,
                destination.Z + 0.5f);
    }

    public ChunkCoord InitialStreamingCenter
    {
        get
        {
            var position =
                InitialPlayerPosition;
            return VoxelCoordinates.FromWorld(
                (int)MathF.Floor(position.X),
                (int)MathF.Floor(position.Y),
                (int)MathF.Floor(position.Z)).Chunk;
        }
    }

    public void BeginRetirement()
    {
        if (_retired)
        {
            throw new ObjectDisposedException(
                nameof(DimensionRuntimeSession));
        }

        if (_retiring)
        {
            return;
        }

        _retiring = true;
        Streaming.BeginRetirement();
        FluidSimulation.BeginRetirement();
        Lighting.BeginRetirement();
        TerrainMesh.BeginRetirement();
        FluidMesh.BeginRetirement();
    }

    public DimensionRetirementDrainReport
        DrainRetirement(
            WorldFrameWorkBudget budget)
    {
        if (!_retiring)
        {
            throw new InvalidOperationException(
                "Dimension session must enter retirement before it can drain.");
        }

        var materialization =
            Residency.CollectMaterializationResults(
                budget,
                Array.Empty<ChunkCoord>());
        var errors =
            new List<Exception>();

        DrainWorker(
            () =>
                Streaming.TryDrainSelection(
                    out var error)
                    ? error
                    : null,
            errors);
        DrainWorker(
            () =>
                FluidSimulation.TryPollCompleted(
                    out _,
                    out var error)
                    ? error
                    : null,
            errors);
        DrainWorker(
            () =>
                FluidMesh.TryPollCompleted(
                    out _,
                    out var error)
                    ? error
                    : null,
            errors);
        DrainWorker(
            () =>
                TerrainMesh.TryPollCompleted(
                    out _,
                    out var error)
                    ? error
                    : null,
            errors);
        DrainWorker(
            () =>
                Lighting.TryPollCompleted(
                    out _,
                    out var error)
                    ? error
                    : null,
            errors);

        return new DimensionRetirementDrainReport(
            materialization,
            errors);
    }

    public DimensionSessionArchiveReport Retire(
        NVector3? playerPosition)
    {
        if (!_retiring ||
            !IsQuiescent)
        {
            throw new InvalidOperationException(
                "Dimension session can retire only after all in-flight work is quiescent.");
        }

        if (_retired)
        {
            throw new InvalidOperationException(
                "Dimension session has already retired.");
        }

        _state.WorldTick =
            WorldTicks.CurrentTick;
        _state.PlayerPosition =
            playerPosition;
        _state.BlockPhysics =
            _blockPhysics.CaptureState();
        _state.DroppedBlocks =
            _droppedBlocks.CaptureState();

        var archive =
            _state.ArchiveResidentWorld();

        Root.QueueFree();
        _retired = true;
        return archive;
    }

    private static void DrainWorker(
        Func<Exception?> poll,
        ICollection<Exception> errors)
    {
        var error =
            poll();

        if (error is not null)
        {
            errors.Add(
                error);
        }
    }
}
