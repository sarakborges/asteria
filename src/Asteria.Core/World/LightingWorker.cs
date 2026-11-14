using System.Diagnostics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public sealed record LightingWorkerResult(
    VoxelLightingDependencies Dependencies,
    VoxelWorld LightingSnapshot,
    WorldLightingBatch SourceBatch,
    VoxelLightingUpdateResult Lighting,
    double WorkerMilliseconds,
    bool Cancelled = false,
    IReadOnlyList<LightingPublicationChunk>? PublicationChunks = null);

public sealed class LightingWorker
{
    private readonly SingleFlightWorker<LightingWorkerResult>? _worker;
    private readonly ChunkGenerationSerialExecutor? _serialExecutor;
    private readonly ChunkGenerationCompletionMailbox _serialCompletionMailbox = new();
    private CancellationTokenSource? _cancellation;
    private VoxelLightingDependencies? _dependencies;
    private ChunkGenerationTaskInstance? _serialTask;
    private ulong _nextSerialGeneration = 1;

    /// <summary>
    /// Compatibility constructor for isolated callers and tests. The game
    /// session replaces this with the shared per-dimension light executor.
    /// </summary>
    public LightingWorker(WorldWorkerPool workerPool)
    {
        _worker = new SingleFlightWorker<LightingWorkerResult>(
            workerPool ?? throw new ArgumentNullException(nameof(workerPool)));
    }

    /// <summary>
    /// Runtime constructor: dynamic relighting shares the same serial LIGHT
    /// lane as initial chunk lighting, while using its own completion mailbox.
    /// </summary>
    public LightingWorker(ChunkGenerationSerialExecutor serialExecutor)
    {
        _serialExecutor = serialExecutor ??
            throw new ArgumentNullException(nameof(serialExecutor));
    }

    private bool IsSerialRunning => _serialTask is not null;

    public bool IsRunning => _serialExecutor is null
        ? _worker!.IsRunning
        : IsSerialRunning;

    // Called only on the authoritative thread. Never read mutable world
    // state from the worker; its snapshot remains isolated.
    public bool CancelIfStale(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!IsRunning || _dependencies is not { } dependencies ||
            dependencies.IsCurrent(world))
            return false;

        _cancellation?.Cancel();
        return true;
    }

    public bool TryStart(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldLightingBatch batch,
        PackContentRegistry<ObjectDefinition>? objects = null) =>
        TryStartScoped(
            world, blocks, fluids, batch, objects, LightingWorkKind.Full);

    internal bool TryStartScoped(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldLightingBatch batch,
        PackContentRegistry<ObjectDefinition>? objects,
        LightingWorkKind workKind,
        WorldWorkerPriority? workerPriority = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.IsEmpty || IsRunning)
            return false;
        if (workKind is not (
                LightingWorkKind.BlockLightOnly or
                LightingWorkKind.SkyOnly or
                LightingWorkKind.Full))
            throw new ArgumentOutOfRangeException(nameof(workKind));

        var snapshot = workKind == LightingWorkKind.BlockLightOnly
            ? VoxelLightingSnapshot.CaptureBlockLightNeighborhood(
                world, batch.EditedPositions)
            : VoxelLightingSnapshot.Capture(
                world, batch.EditedPositions);

        var cancellation = new CancellationTokenSource();
        LightingWorkerResult Execute(CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var lighting = workKind switch
                {
                    LightingWorkKind.BlockLightOnly =>
                        VoxelWorldLightingSolver.RelightBlockLightAfterEdits(
                            snapshot.World, blocks, fluids, batch.EditedPositions,
                            objects, token),
                    LightingWorkKind.SkyOnly =>
                        VoxelWorldLightingSolver.RelightSkyOnlyAfterEdits(
                            snapshot.World, blocks, fluids, batch.EditedPositions,
                            objects, token),
                    _ => VoxelWorldLightingSolver.RelightAfterEdits(
                        snapshot.World, blocks, fluids, batch.EditedPositions,
                        objects, token),
                };
                return new LightingWorkerResult(
                    snapshot.Dependencies, snapshot.World, batch, lighting,
                    stopwatch.Elapsed.TotalMilliseconds,
                    PublicationChunks: LightingPublicationPlan.Create(
                        lighting.ChangedPositions,
                        batch.EditedPositions.Take(1)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // No partial worker light may be copied to the live world.
                return new LightingWorkerResult(
                    snapshot.Dependencies, snapshot.World, batch,
                    new VoxelLightingUpdateResult([], 0),
                    stopwatch.Elapsed.TotalMilliseconds, Cancelled: true);
            }
        }

        var priority = workerPriority ??
            WorldWorkerPriority.FromPriorityClass(
                WorldWorkerPool.InteractiveWorldPriority);
        bool accepted;
        if (_serialExecutor is null)
        {
            accepted = _worker!.TryStart(
                () => Execute(cancellation.Token),
                cancellation.Token,
                priority);
        }
        else
        {
            if (_nextSerialGeneration == 0 ||
                _nextSerialGeneration == ulong.MaxValue)
                throw new InvalidOperationException(
                    "Dynamic-light task generation IDs were exhausted.");

            var firstEdit = batch.EditedPositions[0];
            var seedChunk = VoxelCoordinates.FromWorld(
                firstEdit.X, firstEdit.Y, firstEdit.Z).Chunk;
            var task = new ChunkGenerationTaskInstance(
                new ChunkGenerationTaskKey(
                    ChunkColumnCoord.FromChunk(seedChunk),
                    ChunkGenerationStepId.InitialLighting),
                _nextSerialGeneration++);
            var request = new ChunkGenerationWorkerRequest(
                task,
                WaitToken: 1,
                DependencyId: 1,
                Execute: token => Execute(token),
                CancellationToken: cancellation.Token,
                Priority: priority);

            accepted = _serialExecutor.TryScheduleDependency(
                request, _serialCompletionMailbox);
            if (accepted)
                _serialTask = task;
        }

        if (!accepted)
        {
            cancellation.Dispose();
            return false;
        }

        _cancellation = cancellation;
        _dependencies = snapshot.Dependencies;
        return true;
    }

    public bool TryUpdatePriority(WorldWorkerPriority priority)
    {
        if (_serialExecutor is not null)
            return _serialTask is { } task &&
                   _serialExecutor.TryUpdatePriority(task, priority);

        return _worker!.TryUpdatePriority(priority);
    }

    public bool TryTakeCompleted(
        out LightingWorkerResult? result,
        out Exception? error)
    {
        result = null;
        error = null;

        if (_serialExecutor is not null)
        {
            if (!_serialExecutor.TryCollectDependency(
                    _serialCompletionMailbox, out var completion))
                return false;

            _serialTask = null;
            result = completion.Result as LightingWorkerResult;
            error = completion.Error;
            if (error is null && result is null)
                error = new InvalidOperationException(
                    "Dynamic-light worker returned no lighting result.");
        }
        else
        {
            if (!_worker!.TryTakeCompleted(out result, out error))
                return false;
        }

        _dependencies = null;
        _cancellation?.Dispose();
        _cancellation = null;
        return true;
    }
}
