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
    private readonly Dictionary<ChunkCoord, ChunkPresentation>
        _presentations = [];

    private Task<InitialWorldBuild>? _fixtureTask;
    private Task<WorldUpdateBuild>? _worldUpdateTask;
    private PendingPublication? _pendingPublication;

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

        PollWorldUpdateTask();
        IntegratePendingPublication();
        TryStartWorldUpdate();
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
                $"test-9: multi-chunk meshlet runtime ready; " +
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
            _worldUpdates.EnqueueVoxelEdit(
                world,
                edit.Position);
            TryStartWorldUpdate();
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
            _worldUpdates.EnqueueVoxelEdit(
                world,
                edit.Position);
            TryStartWorldUpdate();
        }
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

    private void TryStartWorldUpdate()
    {
        if (_fixture is null ||
            _worldUpdateTask is not null ||
            _pendingPublication is not null ||
            !_worldUpdates.HasWork)
        {
            return;
        }

        var batch = _worldUpdates.Drain();
        if (batch.IsEmpty)
        {
            return;
        }

        var revision = _fixture.World.Revision;
        var snapshot = _fixture.World.CloneForWorker();
        var blocks = _fixture.Blocks;
        var textures = _terrainTextureLookup;

        _worldUpdateTask = Task.Run(() =>
            BuildWorldUpdate(
                revision,
                snapshot,
                blocks,
                textures,
                batch));
    }

    private static WorldUpdateBuild BuildWorldUpdate(
        ulong revision,
        VoxelWorld snapshot,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        WorldUpdateBatch sourceBatch)
    {
        var stopwatch = Stopwatch.StartNew();
        var lighting =
            VoxelWorldLightingSolver.RelightAfterEdits(
                snapshot,
                blocks,
                sourceBatch.LightingEdits);

        var dirty =
            new Dictionary<ChunkCoord, ChunkMeshletMask>(
                sourceBatch.DirtyMeshlets);

        foreach (var position in lighting.ChangedPositions)
        {
            AddDirtyPosition(
                snapshot,
                dirty,
                position);
        }

        var meshlets = BuildMeshlets(
            snapshot,
            blocks,
            textures,
            dirty);

        stopwatch.Stop();

        return new WorldUpdateBuild(
            revision,
            snapshot,
            sourceBatch,
            dirty,
            meshlets,
            lighting.ChangedPositions.Count,
            lighting.ProcessedVoxelCount,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private void PollWorldUpdateTask()
    {
        if (_worldUpdateTask is null ||
            !_worldUpdateTask.IsCompleted)
        {
            return;
        }

        if (_worldUpdateTask.IsFaulted)
        {
            GD.PushError(
                _worldUpdateTask.Exception?.ToString() ??
                "World update task failed.");
            _worldUpdateTask = null;
            return;
        }

        var result = _worldUpdateTask.Result;
        _worldUpdateTask = null;

        if (_fixture is null)
        {
            return;
        }

        if (result.Revision !=
            _fixture.World.Revision)
        {
            _worldUpdates.Requeue(
                result.SourceBatch);
            TryStartWorldUpdate();
            return;
        }

        _fixture.World.CopyLightFrom(
            result.LightingSnapshot);

        _pendingPublication = new PendingPublication(
            result.Revision,
            result.DirtyMeshlets,
            new Queue<MeshletBuild>(
                result.Meshlets),
            result.WorkerMilliseconds,
            result.LightChangeCount,
            result.LightProcessedVoxelCount);
    }

    private void IntegratePendingPublication()
    {
        if (_pendingPublication is null ||
            _fixture is null)
        {
            return;
        }

        if (_pendingPublication.Revision !=
            _fixture.World.Revision)
        {
            RequeuePendingMeshlets(
                _pendingPublication);
            _pendingPublication = null;
            TryStartWorldUpdate();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var published = 0;

        while (published <
                   MaxMeshletPublishesPerFrame &&
               _pendingPublication.Meshlets.Count > 0)
        {
            var meshlet =
                _pendingPublication.Meshlets.Dequeue();

            if (_presentations.TryGetValue(
                    meshlet.Coord,
                    out var presentation))
            {
                presentation.Apply(
                    meshlet.MeshletIndex,
                    meshlet.Data,
                    _terrainMaterial);
            }

            published++;
        }

        stopwatch.Stop();

        if (published > 0)
        {
            GD.Print(
                $"world.meshlets revision=" +
                $"{_pendingPublication.Revision} " +
                $"published={published} " +
                $"remaining=" +
                $"{_pendingPublication.Meshlets.Count} " +
                $"publish_ms=" +
                $"{stopwatch.Elapsed.TotalMilliseconds:F2}");
        }

        if (_pendingPublication.Meshlets.Count > 0)
        {
            return;
        }

        GD.Print(
            $"world.update revision=" +
            $"{_pendingPublication.Revision} " +
            $"worker_ms=" +
            $"{_pendingPublication.WorkerMilliseconds:F2} " +
            $"light_changes=" +
            $"{_pendingPublication.LightChangeCount} " +
            $"light_processed=" +
            $"{_pendingPublication.LightProcessedVoxelCount}");

        _pendingPublication = null;
        TryStartWorldUpdate();
    }

    private void RequeuePendingMeshlets(
        PendingPublication pending)
    {
        var remaining =
            new Dictionary<ChunkCoord, ChunkMeshletMask>();

        foreach (var meshlet in pending.Meshlets)
        {
            var bit =
                ChunkMeshletMask.Single(
                    meshlet.MeshletIndex);

            remaining[meshlet.Coord] =
                remaining.TryGetValue(
                    meshlet.Coord,
                    out var current)
                    ? current.Union(bit)
                    : bit;
        }

        foreach (var (coord, mask) in remaining)
        {
            _worldUpdates.EnqueueMeshlets(
                coord,
                mask);
        }
    }

    private static void AddDirtyPosition(
        VoxelWorld world,
        IDictionary<ChunkCoord, ChunkMeshletMask> dirty,
        WorldVoxelCoord position)
    {
        VoxelCoordinates.VisitChunkCoordsWhoseVoxelHaloContains(
            position,
            coord =>
            {
                if (!world.ContainsChunk(coord))
                {
                    return;
                }

                var mask =
                    ChunkMeshletMask.ForWorldPosition(
                        coord,
                        position);

                dirty[coord] =
                    dirty.TryGetValue(
                        coord,
                        out var current)
                        ? current.Union(mask)
                        : mask;
            });
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

    private sealed record InitialWorldBuild(
        TestWorldFixture Fixture,
        IReadOnlyList<MeshletBuild> Meshlets,
        double WorkerMilliseconds);

    private sealed record WorldUpdateBuild(
        ulong Revision,
        VoxelWorld LightingSnapshot,
        WorldUpdateBatch SourceBatch,
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
            DirtyMeshlets,
        IReadOnlyList<MeshletBuild> Meshlets,
        int LightChangeCount,
        int LightProcessedVoxelCount,
        double WorkerMilliseconds);

    private sealed record PendingPublication(
        ulong Revision,
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
            DirtyMeshlets,
        Queue<MeshletBuild> Meshlets,
        double WorkerMilliseconds,
        int LightChangeCount,
        int LightProcessedVoxelCount);
}
