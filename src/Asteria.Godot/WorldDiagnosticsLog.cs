using System.Diagnostics;
using System.Globalization;
using Asteria.Core.Diagnostics;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client;

/// <summary>
/// One per-process diagnostics observer. Owns only measurements and the active
/// session log, never gameplay or worker scheduling state.
/// </summary>
internal enum WorldFrameStage : byte
{
    StreamingBegin,
    FluidPoll,
    FluidPublication,
    TerrainPoll,
    TerrainPublication,
    LightingPoll,
    WorkerDispatch,
    StreamingEnd,
    Gameplay,
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
    private long _frames;
    private double _frameMsTotal;
    private double _frameMsMax;
    private long _framesOver50Ms;
    private long _framesOver100Ms;
    private int _materialized;
    private double _materializationMs;
    private double _materializationMaxMs;
    private ChunkCoord? _slowestChunk;
    private int _slowChunks;
    private int _restored;
    private int _terrainMeshBatches;
    private double _terrainMeshMs;
    private int _terrainMeshAccepted;
    private int _terrainMeshStale;
    private int _fluidMeshBatches;
    private double _fluidMeshMs;
    private int _fluidMeshAccepted;
    private int _fluidMeshStale;
    private int _fluidBatches;
    private double _fluidMs;
    private int _fluidChanges;
    private int _fluidBacklogMax;
    private int _lightingBatches;
    private double _lightingMs;
    private int _lightingVoxels;
    private int _lightingChanged;
    private int _lightingDirtyChunks;
    private int _lightingStaleBatches;
    private double _lightingCaptureMs;
    private double _lightingCaptureMaxMs;
    private double _lightingApplyMs;
    private double _lightingApplyMaxMs;

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

    public void ObserveStage(WorldFrameStage stage, long startedTimestamp)
    {
        var milliseconds =
            Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        var index = (int)stage;
        _stageMsTotal[index] += milliseconds;
        _stageMsMax[index] = Math.Max(_stageMsMax[index], milliseconds);
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

    public void ObserveTerrainMesh(double workerMilliseconds, int accepted, int stale)
    {
        _terrainMeshBatches++;
        _terrainMeshMs += workerMilliseconds;
        _terrainMeshAccepted += accepted;
        _terrainMeshStale += stale;
    }

    public void ObserveFluidMesh(double workerMilliseconds, int accepted, int stale)
    {
        _fluidMeshBatches++;
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

        if (report.Kind == LightingCompletionKind.RequeuedStale)
        {
            _lightingStaleBatches++;
            return;
        }

        _lightingBatches++;
        _lightingMs += report.WorkerMilliseconds;
        _lightingVoxels += report.ProcessedVoxelCount;
        _lightingChanged += report.ChangedVoxelCount;
        _lightingDirtyChunks += report.DirtyChunkCount;
        _lightingApplyMs += report.ApplyMilliseconds;
        _lightingApplyMaxMs = Math.Max(
            _lightingApplyMaxMs, report.ApplyMilliseconds);
    }

    public void FlushIfDue(
        int resident, int pending, int materializing,
        int presented, string loadingPhase)
    {
        _resident = resident;
        _pending = pending;
        _inflight = materializing;
        _presented = presented;
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
            $"frames={_frames} frame_ms_avg={Format2((_frames == 0 ? 0d : _frameMsTotal / _frames))} " +
            $"frame_ms_max={Format2(_frameMsMax)} frames_over_50ms={_framesOver50Ms} " +
            $"frames_over_100ms={_framesOver100Ms} " +
            $"generated={_materialized} restored={_restored} " +
            $"generation_ms_total={Format1(_materializationMs)} " +
            $"generation_ms_max={Format1(_materializationMaxMs)} " +
            $"generation_slow_chunks={_slowChunks} slowest_chunk={_slowestChunk} " +
            $"mesh_batches={_terrainMeshBatches} mesh_ms_total={Format1(_terrainMeshMs)} " +
            $"mesh_accepted={_terrainMeshAccepted} mesh_stale={_terrainMeshStale} " +
            $"fluid_mesh_batches={_fluidMeshBatches} fluid_mesh_ms_total={Format1(_fluidMeshMs)} " +
            $"fluid_mesh_accepted={_fluidMeshAccepted} fluid_mesh_stale={_fluidMeshStale} " +
            $"fluid_batches={_fluidBatches} fluid_ms_total={Format1(_fluidMs)} " +
            $"fluid_changes={_fluidChanges} fluid_backlog_max={_fluidBacklogMax} " +
            $"lighting_batches={_lightingBatches} lighting_ms_total={Format1(_lightingMs)} " +
            $"lighting_voxels={_lightingVoxels} lighting_changed={_lightingChanged} " +
            $"lighting_dirty_chunks={_lightingDirtyChunks} " +
            $"lighting_stale={_lightingStaleBatches} " +
            $"lighting_capture_ms_total={Format1(_lightingCaptureMs)} " +
            $"lighting_capture_ms_max={Format1(_lightingCaptureMaxMs)} " +
            $"lighting_apply_ms_total={Format1(_lightingApplyMs)} " +
            $"lighting_apply_ms_max={Format1(_lightingApplyMaxMs)} " +
            stages);

        Array.Clear(_stageMsTotal);
        Array.Clear(_stageMsMax);
        _frames = 0;
        _frameMsTotal = 0d;
        _frameMsMax = 0d;
        _framesOver50Ms = 0;
        _framesOver100Ms = 0;
        _materialized = 0;
        _restored = 0;
        _materializationMs = 0d;
        _materializationMaxMs = 0d;
        _slowChunks = 0;
        _slowestChunk = null;
        _terrainMeshBatches = 0;
        _terrainMeshMs = 0d;
        _terrainMeshAccepted = 0;
        _terrainMeshStale = 0;
        _fluidMeshBatches = 0;
        _fluidMeshMs = 0d;
        _fluidMeshAccepted = 0;
        _fluidMeshStale = 0;
        _fluidBatches = 0;
        _fluidMs = 0d;
        _fluidChanges = 0;
        _fluidBacklogMax = 0;
        _lightingBatches = 0;
        _lightingMs = 0d;
        _lightingVoxels = 0;
        _lightingChanged = 0;
        _lightingDirtyChunks = 0;
        _lightingStaleBatches = 0;
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
