using System.Diagnostics;
using System.Globalization;
using Asteria.Core.Diagnostics;
using Asteria.Core.World;
using Asteria.Client.Rendering;
using Godot;

namespace Asteria.Client;

/// <summary>
/// One per-process diagnostics observer. Owns only measurements and the active
/// session log, never gameplay or worker scheduling state.
/// </summary>
internal enum WorldFrameStage : byte
{
    StreamingBegin,
    CollisionActivation,
    CollisionPrewarm,
    FluidPoll,
    FluidPublication,
    TerrainPoll,
    TerrainPublication,
    LightingPoll,
    WorkerDispatch,
    StreamingEnd,
    Gameplay,
    BlockEntities,
    BlockPhysicsWake,
    FallingSimulation,
    DroppedSimulation,
    BlockEntityPresentation,
    ItemPickup,
    Mining,
    Creatures,
    CreatureSpawn,
    CreatureSimulation,
    CreaturePresentation,
    CreatureParticles,
    CreatureAttacks,
    Projectiles,
    AmbientSimulation,
    AmbientPresentation,
    Hud,
    Portals,
}

internal sealed class WorldDiagnosticsLog : IDisposable
{
    private static readonly long IntervalTicks = 2L * Stopwatch.Frequency;
    private readonly DiagnosticSessionLog _session;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly double[] _stageMsTotal =
        new double[Enum.GetValues<WorldFrameStage>().Length];
    private readonly double[] _stageMsMax =
        new double[Enum.GetValues<WorldFrameStage>().Length];
    private long _nextFlush = Stopwatch.GetTimestamp() + IntervalTicks;
    private bool _disposed;
    private string _phase = "";
    private int _resident;
    private int _pending;
    private int _inflight;
    private int _presented;
    private int _lightingPending;
    private int _terrainMeshPending;
    private int _fluidMeshPending;
    private int _deferredCollisionChunks;
    private int _collisionPrewarmedShapes;
    private int _collisionEmergencyShapes;
    private int _warmPresentations;
    private int _warmPresentationReuses;
    private int _warmPresentationEvictions;
    private int _warmTerrainMeshletsReused;
    private int _warmFluidMeshletsReused;
    private bool _lightingActive;
    private long _frames;
    private double _frameMsTotal;
    private double _frameMsMax;
    private long _framesOver50Ms;
    private long _framesOver100Ms;
    // Sampled viewport statistics identify draw-call pressure separately
    // from CPU mesh construction; values describe rendered passes, not nodes.
    private int _renderSamples;
    private long _renderVisibleCallsTotal;
    private int _renderVisibleCallsMax;
    private long _renderShadowCallsTotal;
    private int _renderShadowCallsMax;
    private int _renderVisibleObjectsMax;
    private int _renderVisiblePrimitivesMax;
    private int _materialized;
    private double _materializationMs;
    private double _terrainGenerationMs;
    private double _initialLightingMs;
    private double _materializationMaxMs;
    private ChunkCoord? _slowestChunk;
    private int _slowChunks;
    private int _restored;
    private int _fluidActivationChunks;
    private int _fluidActivationSlices;
    private int _fluidActivationCells;
    private int _fluidFrontierCells;
    private int _fluidDormantTicksReactivated;
    private int _fluidActivationPendingChunks;
    private double _fluidActivationMs;
    private double _fluidActivationMaxMs;
    private int _terrainMeshBatches;
    private double _terrainMeshMs;
    private double _terrainMeshCaptureMs;
    private double _terrainMeshCaptureMaxMs;
    private int _terrainMeshAccepted;
    private int _terrainMeshStale;
    private int _terrainLightOnlyMeshlets;
    private int _terrainSalvagedMeshlets;
    private int _terrainMeshDiscardedBatches;
    private int _fluidMeshDiscardedBatches;
    private double _lightingDiscardedWorkerMs;
    private double _meshPublicationRenderMs;
    private double _meshPublicationCollisionMs;
    private double _meshPublicationRenderMaxMs;
    private long _meshPublicationVertices;
    private long _meshPublicationCollisionTriangles;
    private int _meshPublicationPublished;
    private int _meshPublicationStale;
    private int _terrainGpuAttributePatches;
    private int _fluidGpuAttributePatches;
    private int _fluidMeshBatches;
    private double _fluidMeshMs;
    private double _fluidMeshCaptureMs;
    private double _fluidMeshCaptureMaxMs;
    private int _fluidMeshAccepted;
    private int _fluidMeshStale;
    private int _fluidLightOnlyMeshlets;
    private int _fluidSalvagedMeshlets;
    private int _fluidBatches;
    private double _fluidMs;
    private int _fluidChanges;
    private int _fluidBacklogMax;
    private int _lightingBatches;
    private int _lightingSeedCount;
    private int _lightingStaleSeedCount;
    private int _lightingSeedBatchMax;
    private double _lightingMs;
    private int _lightingVoxels;
    private int _lightingMediumSampleRequests;
    private int _lightingMediumSampleCacheMisses;
    private int _lightingChanged;
    private int _lightingDirtyChunks;
    private int _lightingStaleBatches;
    private int _lightingStaleContentBatches;
    private int _lightingStaleResidencyBatches;
    private int _lightingStaleUnknownBatches;
    private double _lightingCaptureMs;
    private double _lightingCaptureMaxMs;
    private double _lightingApplyMs;
    private double _lightingApplyMaxMs;
    private int _creaturesActive;
    private int _creaturesPeak;
    private int _projectilesActive;
    private int _creatureParticlesActive;
    private int _creatureParticlesPeak;
    private int _ambientParticlesActive;
    private int _ambientParticlesPeak;
    private int _droppedBlocksActive;
    private int _fallingBlocksActive;
    private int _fallingBlocksWaiting;
    private int _physicsUpdatesPending;
    private int _physicsUpdatesWaiting;
    private int _physicsUpdatesPeak;

    private WorldDiagnosticsLog(DiagnosticSessionLog session)
    {
        _session = session;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static WorldDiagnosticsLog Open()
    {
        var preferred = ProjectSettings.GlobalizePath("res://logs");
        DiagnosticSessionLog session;
        try
        {
            session = DiagnosticSessionLog.Open(preferred);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var fallback = ProjectSettings.GlobalizePath("user://logs");
            GD.PushWarning(
                $"Project log directory unavailable ({exception.Message}); " +
                $"using {fallback}");
            session = DiagnosticSessionLog.Open(fallback);
        }

        var result = new WorldDiagnosticsLog(session);
        result.Write($"diagnostics.ready path={session.Path}");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        result.Write(
            $"build.identity commit={AsteriaBuildIdentity.Commit} " +
            $"mvid={typeof(WorldDiagnosticsLog).Module.ModuleVersionId:N} " +
            $"configuration={configuration}");
        GD.Print($"Asteria session diagnostics: {session.Path}");
        return result;
    }

    public void Write(string message)
    {
        var separator = message.IndexOf(' ');
        var name = separator < 0 ? message : message[..separator];
        var details = separator < 0 ? "" : message[(separator + 1)..];
        _session.Write("INFO", name, details);
    }

    public void Warn(string name, string message) =>
        _session.Write("WARN", name, $"details={message}");

    public void Error(string name, object error) =>
        _session.Write("ERROR", name, $"error={error}");

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        _session.Write(
            "FATAL", "runtime.unhandled_exception",
            $"terminating={args.IsTerminating} exception={args.ExceptionObject}");

    private void OnUnobservedTaskException(
        object? sender, UnobservedTaskExceptionEventArgs args) =>
        _session.Write(
            "ERROR", "runtime.unobserved_task_exception",
            $"exception={args.Exception}");

    public void WorldStarted(ulong seed, DimensionId dimension, ulong dimensionSeed)
    {
        Write(
            $"world.start seed={seed} dimension={dimension} dimension_seed={dimensionSeed}");
    }

    public void ObserveFrame(double deltaSeconds)
    {
        if (deltaSeconds <= 0d)
            return;

        var milliseconds = deltaSeconds * 1000d;
        _frames++;
        _frameMsTotal += milliseconds;
        _frameMsMax = Math.Max(_frameMsMax, milliseconds);
        if (milliseconds >= 50d)
            _framesOver50Ms++;
        if (milliseconds >= 100d)
            _framesOver100Ms++;
    }

    public void ObserveRendering(
        int visibleDrawCalls,
        int shadowDrawCalls,
        int visibleObjects,
        int visiblePrimitives)
    {
        _renderSamples++;
        _renderVisibleCallsTotal += visibleDrawCalls;
        _renderVisibleCallsMax = Math.Max(_renderVisibleCallsMax, visibleDrawCalls);
        _renderShadowCallsTotal += shadowDrawCalls;
        _renderShadowCallsMax = Math.Max(_renderShadowCallsMax, shadowDrawCalls);
        _renderVisibleObjectsMax = Math.Max(_renderVisibleObjectsMax, visibleObjects);
        _renderVisiblePrimitivesMax = Math.Max(
            _renderVisiblePrimitivesMax, visiblePrimitives);
    }

    public void ObserveStage(WorldFrameStage stage, long startedTimestamp) =>
        ObserveStageMilliseconds(
            stage, Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);

    public void ObserveStageMilliseconds(WorldFrameStage stage, double milliseconds)
    {
        var index = (int)stage;
        _stageMsTotal[index] += milliseconds;
        _stageMsMax[index] = Math.Max(_stageMsMax[index], milliseconds);
    }

    /// <summary>
    /// Counts remain cheap to read per frame, with peaks retained for each
    /// diagnostic window so short-lived population spikes are visible.
    /// </summary>
    public void ObserveGameplayPopulation(
        int creatures, int projectiles, int ambientParticles,
        int creatureParticles, int droppedBlocks, int fallingBlocks, int physicsPending,
        int physicsWaiting, int fallingWaiting)
    {
        _creaturesActive = creatures;
        _creaturesPeak = Math.Max(_creaturesPeak, creatures);
        _projectilesActive = projectiles;
        _creatureParticlesActive = creatureParticles;
        _creatureParticlesPeak = Math.Max(_creatureParticlesPeak, creatureParticles);
        _ambientParticlesActive = ambientParticles;
        _ambientParticlesPeak = Math.Max(_ambientParticlesPeak, ambientParticles);
        _droppedBlocksActive = droppedBlocks;
        _fallingBlocksActive = fallingBlocks;
        _fallingBlocksWaiting = fallingWaiting;
        _physicsUpdatesPending = physicsPending;
        _physicsUpdatesWaiting = physicsWaiting;
        _physicsUpdatesPeak = Math.Max(_physicsUpdatesPeak, physicsPending);
    }

    public void Observe(ChunkResidencyUpdate update)
    {
        foreach (var activation in update.Activations)
        {
            if (activation.Source == ChunkActivationSource.Archive)
            {
                _restored++;
                continue;
            }

            _materialized++;
            _materializationMs += activation.WorkerMilliseconds;
            _terrainGenerationMs += activation.TerrainMilliseconds;
            _initialLightingMs += activation.InitialLightingMilliseconds;
            if (activation.WorkerMilliseconds > _materializationMaxMs)
            {
                _materializationMaxMs = activation.WorkerMilliseconds;
                _slowestChunk = activation.Coord;
            }

            if (activation.WorkerMilliseconds >= 200d)
                _slowChunks++;
        }

        foreach (var failure in update.Failures)
            Error("chunk.materialize.failed",
                $"coord={failure.Coord} exception={failure.Error}");
    }

    public void ObserveFluidActivation(FluidActivationWorkReport report)
    {
        _fluidActivationChunks += report.CompletedChunks;
        _fluidActivationSlices += report.SlicesProcessed;
        _fluidActivationCells += report.FluidCellsProcessed;
        _fluidFrontierCells += report.FrontierCellsProcessed;
        _fluidDormantTicksReactivated += report.DormantTicksReactivated;
        _fluidActivationPendingChunks = report.PendingChunks;
        _fluidActivationMs += report.ProcessingMilliseconds;
        _fluidActivationMaxMs = Math.Max(
            _fluidActivationMaxMs,
            report.MaximumSliceMilliseconds);
    }

    public void ObserveTerrainMesh(
        double workerMilliseconds, int accepted, int stale,
        double captureMilliseconds = 0d, bool discarded = false,
        int relitWithoutMeshing = 0, int salvagedFromStaleBatch = 0)
    {
        _terrainSalvagedMeshlets += salvagedFromStaleBatch;
        _terrainMeshBatches++;
        _terrainLightOnlyMeshlets += relitWithoutMeshing;
        if (discarded)
            _terrainMeshDiscardedBatches++;
        _terrainMeshMs += workerMilliseconds;
        _terrainMeshCaptureMs += captureMilliseconds;
        _terrainMeshCaptureMaxMs = Math.Max(
            _terrainMeshCaptureMaxMs, captureMilliseconds);
        _terrainMeshAccepted += accepted;
        _terrainMeshStale += stale;
    }

    public void ObserveWarmPresentations(int cached, int reused, int evicted,
        int reusedTerrainMeshlets, int reusedFluidMeshlets)
    {
        _warmPresentations = cached;
        _warmPresentationReuses += reused;
        _warmPresentationEvictions += evicted;
        _warmTerrainMeshletsReused += reusedTerrainMeshlets;
        _warmFluidMeshletsReused += reusedFluidMeshlets;
    }

    public void ObserveDeferredCollisions(int deferredChunks) =>
        _deferredCollisionChunks = deferredChunks;

    public void ObserveCollisionPrewarm(int builtShapes) =>
        _collisionPrewarmedShapes += builtShapes;

    public void ObserveCollisionEmergencyShapes(int builtShapes) =>
        _collisionEmergencyShapes += builtShapes;

    public void ObserveFluidPublication(ChunkPresentationIntegrationStats stats) =>
        _fluidGpuAttributePatches += stats.GpuAttributePatches;

    public void ObserveTerrainPublication(ChunkPresentationIntegrationStats stats)
    {
        _terrainGpuAttributePatches += stats.GpuAttributePatches;
        _meshPublicationRenderMs += stats.RenderBuildMilliseconds;
        _meshPublicationCollisionMs += stats.CollisionBuildMilliseconds;
        _meshPublicationRenderMaxMs = Math.Max(
            _meshPublicationRenderMaxMs, stats.RenderBuildMilliseconds);
        _meshPublicationVertices += stats.RenderVertices;
        _meshPublicationCollisionTriangles += stats.CollisionTriangles;
        _meshPublicationPublished += stats.Published;
        _meshPublicationStale += stats.Stale;
    }

    public void ObserveFluidMesh(double workerMilliseconds, int accepted, int stale,
        bool discarded = false, int relitWithoutMeshing = 0,
        int salvagedFromStaleBatch = 0, double captureMilliseconds = 0d)
    {
        _fluidSalvagedMeshlets += salvagedFromStaleBatch;
        _fluidMeshBatches++;
        _fluidMeshCaptureMs += captureMilliseconds;
        _fluidMeshCaptureMaxMs = Math.Max(
            _fluidMeshCaptureMaxMs, captureMilliseconds);
        _fluidLightOnlyMeshlets += relitWithoutMeshing;
        if (discarded)
            _fluidMeshDiscardedBatches++;
        _fluidMeshMs += workerMilliseconds;
        _fluidMeshAccepted += accepted;
        _fluidMeshStale += stale;
    }

    public void ObserveFluid(double workerMilliseconds, int changes, int backlog)
    {
        _fluidBatches++;
        _fluidMs += workerMilliseconds;
        _fluidChanges += changes;
        _fluidBacklogMax = Math.Max(_fluidBacklogMax, backlog);
    }

    public void ObserveLighting(LightingRuntimeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        _lightingCaptureMs += report.CaptureMilliseconds;
        _lightingCaptureMaxMs = Math.Max(
            _lightingCaptureMaxMs, report.CaptureMilliseconds);

        _lightingSeedBatchMax = Math.Max(
            _lightingSeedBatchMax, report.SeedCount);
        if (report.Kind == LightingCompletionKind.RequeuedStale)
        {
            _lightingStaleSeedCount += report.SeedCount;
            _lightingStaleBatches++;
            if ((report.StaleDependencies & LightingDependencyChanges.Content) != 0)
                _lightingStaleContentBatches++;
            if ((report.StaleDependencies & LightingDependencyChanges.Residency) != 0)
                _lightingStaleResidencyBatches++;
            if (report.StaleDependencies == LightingDependencyChanges.None)
                _lightingStaleUnknownBatches++;
            _lightingDiscardedWorkerMs += report.WorkerMilliseconds;
            return;
        }

        _lightingBatches++;
        _lightingSeedCount += report.SeedCount;
        _lightingMs += report.WorkerMilliseconds;
        _lightingVoxels += report.ProcessedVoxelCount;
        _lightingMediumSampleRequests += report.MediumSampleRequests;
        _lightingMediumSampleCacheMisses += report.MediumSampleCacheMisses;
        _lightingChanged += report.ChangedVoxelCount;
        _lightingDirtyChunks += report.DirtyChunkCount;
        _lightingApplyMs += report.ApplyMilliseconds;
        _lightingApplyMaxMs = Math.Max(
            _lightingApplyMaxMs, report.ApplyMilliseconds);
    }

    public void FlushIfDue(
        int resident, int pending, int materializing,
        int presented, string loadingPhase,
        int lightingPending, int terrainMeshPending,
        int fluidMeshPending, bool lightingActive)
    {
        _resident = resident;
        _pending = pending;
        _inflight = materializing;
        _presented = presented;
        _lightingPending = lightingPending;
        _terrainMeshPending = terrainMeshPending;
        _fluidMeshPending = fluidMeshPending;
        _lightingActive = lightingActive;
        if (_phase != loadingPhase)
        {
            _phase = loadingPhase;
            Write($"world.phase name={_phase}");
        }

        var now = Stopwatch.GetTimestamp();
        if (now < _nextFlush)
            return;

        _nextFlush = now + IntervalTicks;
        FlushSnapshot();
    }

    private void FlushSnapshot()
    {
        var stages = string.Join(" ",
            Enum.GetValues<WorldFrameStage>().Select(stage =>
            {
                var index = (int)stage;
                var name = stage.ToString().ToLowerInvariant();
                return $"stage_{name}_ms_total={Format1(_stageMsTotal[index])} " +
                    $"stage_{name}_ms_max={Format1(_stageMsMax[index])}";
            }));

        Write(
            $"world.snapshot uptime_s={Format1(_uptime.Elapsed.TotalSeconds)} " +
            $"phase={_phase} resident={_resident} pending={_pending} " +
            $"inflight={_inflight} presented={_presented} " +
            $"collision_deferred_chunks={_deferredCollisionChunks} " +
            $"collision_prewarmed_shapes={_collisionPrewarmedShapes} " +
            $"collision_emergency_shapes={_collisionEmergencyShapes} " +
            $"warm_presentations={_warmPresentations} " +
            $"warm_presentation_reuses={_warmPresentationReuses} " +
            $"warm_presentation_evictions={_warmPresentationEvictions} " +
            $"warm_terrain_meshlets_reused={_warmTerrainMeshletsReused} " +
            $"warm_fluid_meshlets_reused={_warmFluidMeshletsReused} " +
            $"lighting_pending={_lightingPending} " +
            $"terrain_mesh_pending={_terrainMeshPending} " +
            $"fluid_mesh_pending={_fluidMeshPending} lighting_active={_lightingActive} " +
            $"creatures_active={_creaturesActive} creatures_peak={_creaturesPeak} " +
            $"projectiles_active={_projectilesActive} " +
            $"creature_particles_active={_creatureParticlesActive} " +
            $"creature_particles_peak={_creatureParticlesPeak} " +
            $"ambient_particles_active={_ambientParticlesActive} " +
            $"ambient_particles_peak={_ambientParticlesPeak} " +
            $"dropped_blocks_active={_droppedBlocksActive} " +
            $"falling_blocks_active={_fallingBlocksActive} " +
            $"falling_blocks_waiting={_fallingBlocksWaiting} " +
            $"physics_pending={_physicsUpdatesPending} " +
            $"physics_waiting={_physicsUpdatesWaiting} " +
            $"physics_pending_peak={_physicsUpdatesPeak} " +
            $"frames={_frames} frame_ms_avg={Format2((_frames == 0 ? 0d : _frameMsTotal / _frames))} " +
            $"frame_ms_max={Format2(_frameMsMax)} frames_over_50ms={_framesOver50Ms} " +
            $"frames_over_100ms={_framesOver100Ms} " +
            $"render_samples={_renderSamples} " +
            $"render_visible_draw_calls_avg={(_renderSamples == 0 ? 0 : _renderVisibleCallsTotal / _renderSamples)} " +
            $"render_visible_draw_calls_max={_renderVisibleCallsMax} " +
            $"render_shadow_draw_calls_avg={(_renderSamples == 0 ? 0 : _renderShadowCallsTotal / _renderSamples)} " +
            $"render_shadow_draw_calls_max={_renderShadowCallsMax} " +
            $"render_visible_objects_max={_renderVisibleObjectsMax} " +
            $"render_visible_primitives_max={_renderVisiblePrimitivesMax} " +
            $"generated={_materialized} restored={_restored} " +
            $"generation_ms_total={Format1(_materializationMs)} " +
            $"generation_terrain_ms_total={Format1(_terrainGenerationMs)} " +
            $"generation_initial_light_ms_total={Format1(_initialLightingMs)} " +
            $"generation_ms_max={Format1(_materializationMaxMs)} " +
            $"generation_slow_chunks={_slowChunks} slowest_chunk={_slowestChunk} " +
            $"fluid_activation_chunks={_fluidActivationChunks} " +
            $"fluid_activation_slices={_fluidActivationSlices} " +
            $"fluid_activation_cells={_fluidActivationCells} " +
            $"fluid_frontier_cells={_fluidFrontierCells} " +
            $"fluid_dormant_ticks={_fluidDormantTicksReactivated} " +
            $"fluid_activation_pending_chunks={_fluidActivationPendingChunks} " +
            $"fluid_activation_ms_total={Format1(_fluidActivationMs)} " +
            $"fluid_activation_slice_ms_max={Format1(_fluidActivationMaxMs)} " +
            $"mesh_batches={_terrainMeshBatches} mesh_ms_total={Format1(_terrainMeshMs)} " +
            $"mesh_capture_ms_total={Format1(_terrainMeshCaptureMs)} " +
            $"mesh_capture_ms_max={Format1(_terrainMeshCaptureMaxMs)} " +
            $"mesh_accepted={_terrainMeshAccepted} mesh_stale={_terrainMeshStale} " +
            $"mesh_light_only={_terrainLightOnlyMeshlets} " +
            $"mesh_salvaged_from_stale_batch={_terrainSalvagedMeshlets} " +
            $"mesh_discarded_batches={_terrainMeshDiscardedBatches} " +
            $"mesh_publish_count={_meshPublicationPublished} mesh_publish_stale={_meshPublicationStale} " +
            $"mesh_gpu_attribute_patches={_terrainGpuAttributePatches} " +
            $"fluid_gpu_attribute_patches={_fluidGpuAttributePatches} " +
            $"mesh_publish_render_ms_total={Format1(_meshPublicationRenderMs)} " +
            $"mesh_publish_render_ms_max={Format1(_meshPublicationRenderMaxMs)} " +
            $"mesh_publish_collision_ms_total={Format1(_meshPublicationCollisionMs)} " +
            $"mesh_publish_vertices={_meshPublicationVertices} " +
            $"mesh_publish_collision_triangles={_meshPublicationCollisionTriangles} " +
            $"fluid_mesh_batches={_fluidMeshBatches} fluid_mesh_ms_total={Format1(_fluidMeshMs)} " +
            $"fluid_mesh_capture_ms_total={Format1(_fluidMeshCaptureMs)} " +
            $"fluid_mesh_capture_ms_max={Format1(_fluidMeshCaptureMaxMs)} " +
            $"fluid_mesh_accepted={_fluidMeshAccepted} fluid_mesh_stale={_fluidMeshStale} " +
            $"fluid_light_only={_fluidLightOnlyMeshlets} " +
            $"fluid_mesh_salvaged_from_stale_batch={_fluidSalvagedMeshlets} " +
            $"fluid_mesh_discarded_batches={_fluidMeshDiscardedBatches} " +
            $"fluid_batches={_fluidBatches} fluid_ms_total={Format1(_fluidMs)} " +
            $"fluid_changes={_fluidChanges} fluid_backlog_max={_fluidBacklogMax} " +
            $"lighting_batches={_lightingBatches} lighting_ms_total={Format1(_lightingMs)} " +
            $"lighting_seed_count={_lightingSeedCount} " +
            $"lighting_stale_seed_count={_lightingStaleSeedCount} " +
            $"lighting_seed_batch_max={_lightingSeedBatchMax} " +
            $"lighting_voxels={_lightingVoxels} " +
            $"lighting_medium_cache_queries={_lightingMediumSampleRequests} " +
            $"lighting_medium_cache_misses={_lightingMediumSampleCacheMisses} " +
            $"lighting_changed={_lightingChanged} " +
            $"lighting_dirty_chunks={_lightingDirtyChunks} " +
            $"lighting_stale={_lightingStaleBatches} " +
            $"lighting_stale_content={_lightingStaleContentBatches} " +
            $"lighting_stale_residency={_lightingStaleResidencyBatches} " +
            $"lighting_stale_unknown={_lightingStaleUnknownBatches} " +
            $"lighting_discarded_worker_ms_total={Format1(_lightingDiscardedWorkerMs)} " +
            $"lighting_capture_ms_total={Format1(_lightingCaptureMs)} " +
            $"lighting_capture_ms_max={Format1(_lightingCaptureMaxMs)} " +
            $"lighting_apply_ms_total={Format1(_lightingApplyMs)} " +
            $"lighting_apply_ms_max={Format1(_lightingApplyMaxMs)} " +
            stages);

        Array.Clear(_stageMsTotal);
        Array.Clear(_stageMsMax);
        _creaturesPeak = _creaturesActive;
        _creatureParticlesPeak = _creatureParticlesActive;
        _ambientParticlesPeak = _ambientParticlesActive;
        _physicsUpdatesPeak = _physicsUpdatesPending;
        _collisionPrewarmedShapes = 0;
        _collisionEmergencyShapes = 0;
        _warmPresentationReuses = 0;
        _warmPresentationEvictions = 0;
        _warmTerrainMeshletsReused = 0;
        _warmFluidMeshletsReused = 0;
        _frames = 0;
        _frameMsTotal = 0d;
        _frameMsMax = 0d;
        _framesOver50Ms = 0;
        _framesOver100Ms = 0;
        _renderSamples = 0;
        _renderVisibleCallsTotal = 0;
        _renderVisibleCallsMax = 0;
        _renderShadowCallsTotal = 0;
        _renderShadowCallsMax = 0;
        _renderVisibleObjectsMax = 0;
        _renderVisiblePrimitivesMax = 0;
        _materialized = 0;
        _restored = 0;
        _fluidActivationChunks = 0;
        _fluidActivationSlices = 0;
        _fluidActivationCells = 0;
        _fluidFrontierCells = 0;
        _fluidDormantTicksReactivated = 0;
        _fluidActivationPendingChunks = 0;
        _fluidActivationMs = 0d;
        _fluidActivationMaxMs = 0d;
        _materializationMs = 0d;
        _terrainGenerationMs = 0d;
        _initialLightingMs = 0d;
        _materializationMaxMs = 0d;
        _slowChunks = 0;
        _slowestChunk = null;
        _terrainMeshBatches = 0;
        _terrainMeshMs = 0d;
        _terrainMeshCaptureMs = 0d;
        _terrainMeshCaptureMaxMs = 0d;
        _terrainMeshAccepted = 0;
        _terrainMeshStale = 0;
        _terrainLightOnlyMeshlets = 0;
        _terrainSalvagedMeshlets = 0;
        _terrainMeshDiscardedBatches = 0;
        _fluidMeshDiscardedBatches = 0;
        _lightingDiscardedWorkerMs = 0d;
        _meshPublicationRenderMs = 0d;
        _meshPublicationCollisionMs = 0d;
        _meshPublicationRenderMaxMs = 0d;
        _meshPublicationVertices = 0;
        _meshPublicationCollisionTriangles = 0;
        _meshPublicationPublished = 0;
        _meshPublicationStale = 0;
        _terrainGpuAttributePatches = 0;
        _fluidGpuAttributePatches = 0;
        _fluidMeshBatches = 0;
        _fluidMeshMs = 0d;
        _fluidMeshCaptureMs = 0d;
        _fluidMeshCaptureMaxMs = 0d;
        _fluidMeshAccepted = 0;
        _fluidMeshStale = 0;
        _fluidLightOnlyMeshlets = 0;
        _fluidSalvagedMeshlets = 0;
        _fluidBatches = 0;
        _fluidMs = 0d;
        _fluidChanges = 0;
        _fluidBacklogMax = 0;
        _lightingBatches = 0;
        _lightingSeedCount = 0;
        _lightingStaleSeedCount = 0;
        _lightingSeedBatchMax = 0;
        _lightingMs = 0d;
        _lightingVoxels = 0;
        _lightingMediumSampleRequests = 0;
        _lightingMediumSampleCacheMisses = 0;
        _lightingChanged = 0;
        _lightingDirtyChunks = 0;
        _lightingStaleBatches = 0;
        _lightingStaleContentBatches = 0;
        _lightingStaleResidencyBatches = 0;
        _lightingStaleUnknownBatches = 0;
        _lightingCaptureMs = 0d;
        _lightingCaptureMaxMs = 0d;
        _lightingApplyMs = 0d;
        _lightingApplyMaxMs = 0d;
    }

    private static string Format1(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture);

    private static string Format2(double value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        try
        {
            FlushSnapshot(); // Short runs still retain their final measurements.
        }
        finally
        {
            _session.Dispose();
        }
    }
}
