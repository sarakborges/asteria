using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Client.Rendering;
using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client;

public partial class Main : Node3D
{
    private const float InteractionDistance = 6f;
    private const int MaxMeshletPublishesPerFrame = 4;

    private readonly WorldUpdateQueue _worldUpdates = new();
    private readonly MeshletContentRevisions _contentRevisions = new();
    private readonly Dictionary<ChunkCoord, ChunkPresentation>
        _presentations = [];
    private readonly Queue<MeshletPublication>
        _pendingMeshletPublications = new();

    private Task<InitialWorldBuild>? _fixtureTask;
    private Task<MeshUpdateBuild>? _meshTask;
    private Task<LightingUpdateBuild>? _lightingTask;

    private TestWorldFixture? _fixture;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private TerrainTextureLookup _terrainTextureLookup = null!;
    private ShaderMaterial _terrainMaterial = null!;
    private BlockRuntimeId _placementBlock;
    private bool _worldAttached;
    private bool _fixtureErrorReported;

    public override void _Ready()
    {
        SetupWebUi();

        var blocks = BlockContentLoader.LoadProjectBlocks();
        _terrainTextures = TerrainTextureCatalog.Create(blocks);
        _terrainTextureLookup = _terrainTextures.CreateLookup();
        _terrainMaterial = VoxelTerrainMaterial.Create(_terrainTextures);

        GD.Print(
            $"block content: loaded {blocks.AuthoredCount} definitions, " +
            $"{_terrainTextures.TextureCount} terrain textures");

        var textures = _terrainTextureLookup;

        // Deterministic multi-chunk QA fixture. Still not world generation.
        _fixtureTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var fixture = TestWorldFactory.Create(blocks);
            VoxelWorldLightingSolver.Initialize(
                fixture.World,
                blocks);
            var meshlets = BuildAllMeshlets(
                fixture.World,
                blocks,
                textures);
            stopwatch.Stop();

            return new InitialWorldBuild(
                fixture,
                meshlets,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    public override void _Process(double delta)
    {
        if (!_worldAttached)
        {
            PollFixture();
            return;
        }

        PollMeshTask();
        IntegrateMeshletPublications();
        PollLightingTask();

        // Geometry is deliberately dispatched before lighting. A content edit
        // should disappear/appear as soon as its meshlet is rebuilt; lighting
        // catches up independently, matching Mineclone's separate queues.
        TryStartMeshTask();
        TryStartLightingTask();
    }

    private void PollFixture()
    {
        if (_fixtureTask is null)
        {
            return;
        }

        if (_fixtureTask.IsCompletedSuccessfully)
        {
            var result = _fixtureTask.Result;
            _fixture = result.Fixture;
            _placementBlock =
                _fixture.Blocks.GetId(TestChunkFactory.StoneId);

            AttachWorld(result.Meshlets);
            SetupPlayer();
            _worldAttached = true;

            var voxelCount = CountVoxels(_fixture.World);
            var triangleCount = result.Meshlets.Sum(
                meshlet => meshlet.Data.TriangleCount);

            GD.Print(
                $"test-10: split geometry/lighting meshlet runtime ready; " +
                $"chunks={_fixture.World.ChunkCount}, " +
                $"voxels={voxelCount}, " +
                $"triangles={triangleCount}, " +
                $"worker_ms={result.WorkerMilliseconds:F2}");

            SendWorldReady();
            SendWebUi(
                "game.player_ready",
                new { controller = "fps" });
            return;
        }

        if (_fixtureTask.IsFaulted &&
            !_fixtureErrorReported)
        {
            _fixtureErrorReported = true;
            GD.PushError(
                _fixtureTask.Exception?.ToString() ??
                "Test world fixture failed.");
        }
    }

    private void SetupWebUi()
    {
        _webUi = GetNode<Node>("WebUi");
        _webUi.Connect(
            "message_received",
            Callable.From<string>(OnWebUiMessage));
        _webUi.Connect(
            "webui_ready",
            Callable.From(OnWebUiReady));
    }

    private void OnWebUiReady()
    {
        SendCurrentState();
    }

    private void OnWebUiMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            if (!document.RootElement.TryGetProperty(
                    "type",
                    out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            GD.Print($"webui -> godot: {type}");

            switch (type)
            {
                case "ui.ready":
                    SendCurrentState();
                    break;
                case "ui.ping":
                    SendWebUi(
                        "game.pong",
                        new { timestamp = Time.GetTicksMsec() });
                    break;
            }
        }
        catch (JsonException exception)
        {
            GD.PushWarning(
                $"Ignoring invalid WebUI message: " +
                $"{exception.Message}");
        }
    }

    private void SendCurrentState()
    {
        SendWebUi(
            "game.ready",
            new { bridge = 1, engine = "godot" });

        if (_worldAttached && _fixture is not null)
        {
            SendWorldReady();
        }

        if (_player is not null)
        {
            SendWebUi(
                "game.player_ready",
                new { controller = "fps" });
            SendMouseCaptureState(
                _player.IsMouseCaptured);
        }
    }

    private void SendWorldReady()
    {
        if (_fixture is null)
        {
            return;
        }

        SendWebUi(
            "game.chunk_ready",
            new
            {
                size = Chunk.Size,
                chunks = _fixture.World.ChunkCount,
                voxels = CountVoxels(_fixture.World),
                blocks = _fixture.Blocks.AuthoredCount,
                textures = _terrainTextures.TextureCount,
                meshletsPerChunk = ChunkMeshletMask.Count,
            });
    }

    private void SendWebUi(
        string type,
        object payload)
    {
        _webUi.Call(
            "post_message",
            JsonSerializer.Serialize(
                new { type, payload }));
    }

    private void SendMouseCaptureState(bool captured)
    {
        _webUi.Call(
            "set_mouse_captured",
            captured);
        SendWebUi(
            "game.mouse_capture",
            new { captured });
    }

    private void AttachWorld(
        IReadOnlyList<MeshletBuild> meshlets)
    {
        if (_fixture is null)
        {
            return;
        }

        foreach (var coord in _fixture.World.LoadedChunkCoords
                     .OrderBy(coord => coord.Y)
                     .ThenBy(coord => coord.Z)
                     .ThenBy(coord => coord.X))
        {
            var presentation = new ChunkPresentation(coord);
            _presentations.Add(coord, presentation);
            AddChild(presentation.Root);
        }

        foreach (var meshlet in meshlets)
        {
            _presentations[meshlet.Coord].Apply(
                meshlet.MeshletIndex,
                meshlet.Data,
                _terrainMaterial);
        }
    }

    private void SetupPlayer()
    {
        _player = new FpsPlayer
        {
            Name = "Player",
            Position = new Vector3(0f, 20f, 0f),
        };

        _player.BreakRequested += BreakTargetBlock;
        _player.PlaceRequested += PlaceTargetBlock;
        _player.MouseCaptureChanged +=
            SendMouseCaptureState;
        AddChild(_player);
    }

    private void BreakTargetBlock()
    {
        if (!TryGetTarget(
                out var hitVoxel,
                out _))
        {
            return;
        }

        var world = _fixture!.World;
        var cell = world.GetCellOrEmpty(hitVoxel);
        if (cell.IsEmpty)
        {
            return;
        }

        _placementBlock = cell.Block;

        if (world.SetBlockAt(
                hitVoxel,
                BlockRuntimeId.Air,
                out var edit))
        {
            QueueVoxelEdit(edit.Position);
        }
    }

    private void PlaceTargetBlock()
    {
        if (!TryGetTarget(
                out var hitVoxel,
                out var faceOffset))
        {
            return;
        }

        var target = hitVoxel + faceOffset;
        var world = _fixture!.World;

        if (!world.IsLoadedAt(target) ||
            !world.GetCellOrEmpty(target).IsEmpty)
        {
            return;
        }

        var worldVoxelMin = new Vector3(
            target.X,
            target.Y,
            target.Z);

        if (_player!.IntersectsVoxelAabb(
                worldVoxelMin))
        {
            return;
        }

        if (world.SetBlockAt(
                target,
                _placementBlock,
                out var edit))
        {
            QueueVoxelEdit(edit.Position);
        }
    }

    private void QueueVoxelEdit(WorldVoxelCoord position)
    {
        var world = _fixture!.World;

        _contentRevisions.BumpVoxelEdit(
            world,
            position);
        _worldUpdates.EnqueueVoxelEdit(
            world,
            position);

        TryStartMeshTask();
        TryStartLightingTask();
    }

    private bool TryGetTarget(
        out WorldVoxelCoord hitVoxel,
        out (int X, int Y, int Z) faceOffset)
    {
        hitVoxel = default;
        faceOffset = default;

        if (_player is null || _fixture is null)
        {
            return false;
        }

        var (from, to) =
            _player.GetInteractionRay(
                InteractionDistance);
        var direction = to - from;

        var hit = VoxelWorldRaycaster.Raycast(
            _fixture.World,
            _fixture.Blocks,
            new NVector3(from.X, from.Y, from.Z),
            new NVector3(
                direction.X,
                direction.Y,
                direction.Z),
            InteractionDistance);

        if (hit is null ||
            !hit.Value.HasSurfaceNormal)
        {
            return false;
        }

        hitVoxel = hit.Value.Voxel;
        faceOffset = (
            hit.Value.NormalX,
            hit.Value.NormalY,
            hit.Value.NormalZ);
        return true;
    }

    private void TryStartMeshTask()
    {
        if (_fixture is null ||
            _meshTask is not null ||
            !_worldUpdates.HasMeshWork)
        {
            return;
        }

        var batch = _worldUpdates.DrainMeshlets();
        if (batch.IsEmpty)
        {
            return;
        }

        var snapshot = _fixture.World.CloneForWorker();
        var blocks = _fixture.Blocks;
        var textures = _terrainTextureLookup;
        var revisions =
            _contentRevisions.Capture(
                batch.DirtyMeshlets);

        _meshTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var meshlets = BuildMeshlets(
                snapshot,
                blocks,
                textures,
                batch.DirtyMeshlets);
            stopwatch.Stop();

            return new MeshUpdateBuild(
                batch,
                revisions,
                meshlets,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    private void PollMeshTask()
    {
        if (_meshTask is null ||
            !_meshTask.IsCompleted)
        {
            return;
        }

        if (_meshTask.IsFaulted)
        {
            GD.PushError(
                _meshTask.Exception?.ToString() ??
                "Meshlet geometry task failed.");
            _meshTask = null;
            return;
        }

        var result = _meshTask.Result;
        _meshTask = null;
        var accepted = 0;
        var stale = 0;

        foreach (var meshlet in result.Meshlets)
        {
            var key = new ChunkMeshletKey(
                meshlet.Coord,
                meshlet.MeshletIndex);
            var revision =
                result.ContentRevisions[key];

            if (_contentRevisions.IsCurrent(
                    key,
                    revision))
            {
                _pendingMeshletPublications.Enqueue(
                    new MeshletPublication(
                        meshlet,
                        revision));
                accepted++;
            }
            else
            {
                _worldUpdates.EnqueueMeshlets(
                    meshlet.Coord,
                    ChunkMeshletMask.Single(
                        meshlet.MeshletIndex));
                stale++;
            }
        }

        GD.Print(
            $"world.geometry worker_ms=" +
            $"{result.WorkerMilliseconds:F2} " +
            $"accepted={accepted} stale={stale}");

        TryStartMeshTask();
    }

    private void TryStartLightingTask()
    {
        if (_fixture is null ||
            _lightingTask is not null ||
            !_worldUpdates.HasLightingWork)
        {
            return;
        }

        var batch = _worldUpdates.DrainLighting();
        if (batch.IsEmpty)
        {
            return;
        }

        var worldRevision = _fixture.World.Revision;
        var snapshot = _fixture.World.CloneForWorker();
        var blocks = _fixture.Blocks;

        _lightingTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var lighting =
                VoxelWorldLightingSolver.RelightAfterEdits(
                    snapshot,
                    blocks,
                    batch.EditedPositions);
            stopwatch.Stop();

            return new LightingUpdateBuild(
                worldRevision,
                snapshot,
                batch,
                lighting,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    private void PollLightingTask()
    {
        if (_lightingTask is null ||
            !_lightingTask.IsCompleted)
        {
            return;
        }

        if (_lightingTask.IsFaulted)
        {
            GD.PushError(
                _lightingTask.Exception?.ToString() ??
                "Incremental lighting task failed.");
            _lightingTask = null;
            return;
        }

        var result = _lightingTask.Result;
        _lightingTask = null;

        if (_fixture is null)
        {
            return;
        }

        if (result.WorldRevision !=
            _fixture.World.Revision)
        {
            _worldUpdates.RequeueLighting(
                result.SourceBatch);
            TryStartLightingTask();
            return;
        }

        _fixture.World.CopyLightFrom(
            result.LightingSnapshot);

        foreach (var position in
                 result.Lighting.ChangedPositions)
        {
            // Lighting dirties presentation, but it does not invalidate
            // content geometry already built for the same meshlet. A follow-up
            // mesh pass refreshes vertex light independently.
            _worldUpdates.EnqueueVoxelMeshlets(
                _fixture.World,
                position);
        }

        GD.Print(
            $"world.lighting worker_ms=" +
            $"{result.WorkerMilliseconds:F2} " +
            $"light_changes=" +
            $"{result.Lighting.ChangedPositions.Count} " +
            $"light_processed=" +
            $"{result.Lighting.ProcessedVoxelCount}");

        TryStartMeshTask();
        TryStartLightingTask();
    }

    private void IntegrateMeshletPublications()
    {
        if (_fixture is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var published = 0;
        var stale = 0;

        while (published + stale <
                   MaxMeshletPublishesPerFrame &&
               _pendingMeshletPublications.Count > 0)
        {
            var pending =
                _pendingMeshletPublications.Dequeue();
            var key = new ChunkMeshletKey(
                pending.Meshlet.Coord,
                pending.Meshlet.MeshletIndex);

            if (!_contentRevisions.IsCurrent(
                    key,
                    pending.ContentRevision))
            {
                _worldUpdates.EnqueueMeshlets(
                    pending.Meshlet.Coord,
                    ChunkMeshletMask.Single(
                        pending.Meshlet.MeshletIndex));
                stale++;
                continue;
            }

            if (_presentations.TryGetValue(
                    pending.Meshlet.Coord,
                    out var presentation))
            {
                presentation.Apply(
                    pending.Meshlet.MeshletIndex,
                    pending.Meshlet.Data,
                    _terrainMaterial);
            }

            published++;
        }

        stopwatch.Stop();

        if (published > 0 || stale > 0)
        {
            GD.Print(
                $"world.meshlets published={published} " +
                $"stale={stale} " +
                $"remaining=" +
                $"{_pendingMeshletPublications.Count} " +
                $"publish_ms=" +
                $"{stopwatch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    private static List<MeshletBuild> BuildAllMeshlets(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures)
    {
        var dirty =
            world.LoadedChunkCoords.ToDictionary(
                coord => coord,
                _ => ChunkMeshletMask.All);

        return BuildMeshlets(
            world,
            blocks,
            textures,
            dirty);
    }

    private static List<MeshletBuild> BuildMeshlets(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty)
    {
        var result = new List<MeshletBuild>();

        foreach (var (coord, mask) in dirty
                     .OrderBy(entry => entry.Key.Y)
                     .ThenBy(entry => entry.Key.Z)
                     .ThenBy(entry => entry.Key.X))
        {
            foreach (var meshletIndex in mask.Indices())
            {
                result.Add(
                    new MeshletBuild(
                        coord,
                        meshletIndex,
                        ChunkMeshDataBuilder.BuildMeshlet(
                            world,
                            coord,
                            blocks,
                            textures,
                            meshletIndex)));
            }
        }

        return result;
    }

    private static int CountVoxels(
        VoxelWorld world) =>
        world.LoadedChunkCoords.Sum(
            coord =>
                world.GetChunk(coord)
                    .NonEmptyVoxelCount);

    private sealed record MeshletBuild(
        ChunkCoord Coord,
        int MeshletIndex,
        ChunkMeshData Data);

    private sealed record MeshletPublication(
        MeshletBuild Meshlet,
        ulong ContentRevision);

    private sealed record InitialWorldBuild(
        TestWorldFixture Fixture,
        IReadOnlyList<MeshletBuild> Meshlets,
        double WorkerMilliseconds);

    private sealed record MeshUpdateBuild(
        WorldMeshBatch SourceBatch,
        IReadOnlyDictionary<ChunkMeshletKey, ulong>
            ContentRevisions,
        IReadOnlyList<MeshletBuild> Meshlets,
        double WorkerMilliseconds);

    private sealed record LightingUpdateBuild(
        ulong WorldRevision,
        VoxelWorld LightingSnapshot,
        WorldLightingBatch SourceBatch,
        VoxelLightingUpdateResult Lighting,
        double WorkerMilliseconds);
}
