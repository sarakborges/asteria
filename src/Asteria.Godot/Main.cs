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

    private Task<TestChunkFixture>? _fixtureTask;
    private Task<ChunkPresentationBuild>? _presentationTask;
    private TestChunkFixture? _fixture;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private TerrainTextureLookup _terrainTextureLookup = null!;
    private ShaderMaterial _terrainMaterial = null!;
    private Node3D? _chunkRoot;
    private MeshInstance3D? _chunkMesh;
    private CollisionShape3D? _chunkCollision;
    private BlockRuntimeId _placementBlock;
    private ChunkMeshData? _pendingCollisionData;
    private ulong _pendingCollisionRevision;
    private bool _deferCollisionOneFrame;
    private bool _chunkAttached;
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

        // Temporary QA fixture only. This is not a world-generation path.
        _fixtureTask = Task.Run(() =>
        {
            var fixture = TestChunkFactory.Create(blocks);
            ChunkLightingSolver.Initialize(fixture.Chunk, blocks);
            return fixture;
        });
    }

    public override void _Process(double delta)
    {
        if (!_chunkAttached)
        {
            PollFixture();
            return;
        }

        PollPresentationBuild();
        PublishDeferredCollision();
    }

    private void PollFixture()
    {
        if (_fixtureTask is null)
        {
            return;
        }

        if (_fixtureTask.IsCompletedSuccessfully)
        {
            _fixture = _fixtureTask.Result;
            _placementBlock = _fixture.Blocks.GetId(TestChunkFactory.StoneId);

            var meshData = ChunkMeshDataBuilder.Build(
                _fixture.Chunk,
                _fixture.Blocks,
                _terrainTextureLookup);

            AttachChunk(meshData);
            SetupPlayer();
            _chunkAttached = true;

            GD.Print(
                $"test-8: queued voxel edits + DDA targeting ready; " +
                $"voxels={_fixture.Chunk.NonEmptyVoxelCount}, " +
                $"triangles={meshData.TriangleCount}");
            SendWebUi("game.chunk_ready", new
            {
                size = Chunk.Size,
                voxels = _fixture.Chunk.NonEmptyVoxelCount,
                paletteEntries = _fixture.Chunk.PaletteEntryCount,
                blocks = _fixture.Blocks.AuthoredCount,
                textures = _terrainTextures.TextureCount,
            });
            SendWebUi("game.player_ready", new { controller = "fps" });
            return;
        }

        if (_fixtureTask.IsFaulted && !_fixtureErrorReported)
        {
            _fixtureErrorReported = true;
            GD.PushError(_fixtureTask.Exception?.ToString() ?? "Test chunk fixture failed.");
        }
    }

    private void SetupWebUi()
    {
        _webUi = GetNode<Node>("WebUi");
        _webUi.Connect("message_received", Callable.From<string>(OnWebUiMessage));
        _webUi.Connect("webui_ready", Callable.From(OnWebUiReady));
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
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
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
                    SendWebUi("game.pong", new { timestamp = Time.GetTicksMsec() });
                    break;
            }
        }
        catch (JsonException exception)
        {
            GD.PushWarning($"Ignoring invalid WebUI message: {exception.Message}");
        }
    }

    private void SendCurrentState()
    {
        SendWebUi("game.ready", new { bridge = 1, engine = "godot" });

        if (_chunkAttached && _fixture is not null)
        {
            SendWebUi("game.chunk_ready", new
            {
                size = Chunk.Size,
                voxels = _fixture.Chunk.NonEmptyVoxelCount,
                paletteEntries = _fixture.Chunk.PaletteEntryCount,
                blocks = _fixture.Blocks.AuthoredCount,
                textures = _terrainTextures.TextureCount,
            });
        }

        if (_player is not null)
        {
            SendWebUi("game.player_ready", new { controller = "fps" });
            SendMouseCaptureState(_player.IsMouseCaptured);
        }
    }

    private void SendWebUi(string type, object payload)
    {
        _webUi.Call("post_message", JsonSerializer.Serialize(new { type, payload }));
    }

    private void SendMouseCaptureState(bool captured)
    {
        _webUi.Call("set_mouse_captured", captured);
        SendWebUi("game.mouse_capture", new { captured });
    }

    private void AttachChunk(ChunkMeshData meshData)
    {
        var mesh = ChunkMeshBuilder.CreateMesh(meshData, _terrainMaterial);

        _chunkRoot = new Node3D
        {
            Name = "TestChunk",
            Position = new Vector3(-Chunk.Size / 2f, 0f, -Chunk.Size / 2f),
        };

        _chunkMesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
        };

        var staticBody = new StaticBody3D
        {
            Name = "Collision",
        };

        _chunkCollision = new CollisionShape3D
        {
            Name = "Shape",
            Shape = ChunkMeshBuilder.CreateCollisionShape(meshData),
        };

        staticBody.AddChild(_chunkCollision);
        _chunkRoot.AddChild(_chunkMesh);
        _chunkRoot.AddChild(staticBody);
        AddChild(_chunkRoot);
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
        _player.MouseCaptureChanged += SendMouseCaptureState;
        AddChild(_player);
    }

    private void BreakTargetBlock()
    {
        if (!TryGetTarget(out var hitVoxel, out _))
        {
            return;
        }

        var chunk = _fixture!.Chunk;
        var cell = chunk.GetCell(hitVoxel.X, hitVoxel.Y, hitVoxel.Z);
        if (cell.IsEmpty)
        {
            return;
        }

        _placementBlock = cell.Block;
        if (chunk.SetBlock(hitVoxel.X, hitVoxel.Y, hitVoxel.Z, BlockRuntimeId.Air))
        {
            QueuePresentationRebuild();
        }
    }

    private void PlaceTargetBlock()
    {
        if (!TryGetTarget(out var hitVoxel, out var faceOffset))
        {
            return;
        }

        var target = hitVoxel + faceOffset;
        if (!Chunk.Contains(target.X, target.Y, target.Z))
        {
            return;
        }

        var chunk = _fixture!.Chunk;
        if (!chunk.GetCell(target.X, target.Y, target.Z).IsEmpty)
        {
            return;
        }

        var worldVoxelMin = _chunkRoot!.ToGlobal(
            new Vector3(target.X, target.Y, target.Z));
        if (_player!.IntersectsVoxelAabb(worldVoxelMin))
        {
            return;
        }

        if (chunk.SetBlock(target.X, target.Y, target.Z, _placementBlock))
        {
            QueuePresentationRebuild();
        }
    }

    private bool TryGetTarget(out Vector3I hitVoxel, out Vector3I faceOffset)
    {
        hitVoxel = default;
        faceOffset = default;

        if (_player is null || _chunkRoot is null || _fixture is null)
        {
            return false;
        }

        var (worldFrom, worldTo) = _player.GetInteractionRay(InteractionDistance);
        var localFrom = _chunkRoot.ToLocal(worldFrom);
        var localTo = _chunkRoot.ToLocal(worldTo);
        var direction = localTo - localFrom;

        var hit = ChunkVoxelRaycaster.Raycast(
            _fixture.Chunk,
            _fixture.Blocks,
            new NVector3(localFrom.X, localFrom.Y, localFrom.Z),
            new NVector3(direction.X, direction.Y, direction.Z),
            InteractionDistance);

        if (hit is null || !hit.Value.HasSurfaceNormal)
        {
            return false;
        }

        var voxel = hit.Value.Voxel;
        hitVoxel = new Vector3I(voxel.X, voxel.Y, voxel.Z);
        faceOffset = new Vector3I(
            hit.Value.NormalX,
            hit.Value.NormalY,
            hit.Value.NormalZ);
        return true;
    }

    private void QueuePresentationRebuild()
    {
        if (_presentationTask is null)
        {
            StartPresentationBuild();
        }
    }

    private void StartPresentationBuild()
    {
        if (_fixture is null)
        {
            return;
        }

        var revision = _fixture.Chunk.Revision;
        var snapshot = _fixture.Chunk.CloneForWorker();
        var blocks = _fixture.Blocks;
        var textures = _terrainTextureLookup;

        _presentationTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            ChunkLightingSolver.Initialize(snapshot, blocks);
            var meshData = ChunkMeshDataBuilder.Build(snapshot, blocks, textures);
            stopwatch.Stop();

            return new ChunkPresentationBuild(
                revision,
                snapshot,
                meshData,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    private void PollPresentationBuild()
    {
        if (_presentationTask is null || !_presentationTask.IsCompleted)
        {
            return;
        }

        if (_presentationTask.IsFaulted)
        {
            GD.PushError(
                _presentationTask.Exception?.ToString() ??
                "Queued chunk presentation build failed.");
            _presentationTask = null;
            return;
        }

        var result = _presentationTask.Result;
        _presentationTask = null;

        if (_fixture is null)
        {
            return;
        }

        if (result.Revision == _fixture.Chunk.Revision)
        {
            PublishPresentation(result);
        }

        if (result.Revision != _fixture.Chunk.Revision)
        {
            StartPresentationBuild();
        }
    }

    private void PublishPresentation(ChunkPresentationBuild result)
    {
        if (_fixture is null || _chunkMesh is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        _fixture.Chunk.CopyLightFrom(result.LightingSnapshot);
        _chunkMesh.Mesh = ChunkMeshBuilder.CreateMesh(
            result.MeshData,
            _terrainMaterial);

        _pendingCollisionData = result.MeshData;
        _pendingCollisionRevision = result.Revision;
        _deferCollisionOneFrame = true;

        stopwatch.Stop();
        GD.Print(
            $"chunk.presentation revision={result.Revision} " +
            $"worker_ms={result.WorkerMilliseconds:F2} " +
            $"publish_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"triangles={result.MeshData.TriangleCount}");
    }

    private void PublishDeferredCollision()
    {
        if (_pendingCollisionData is null ||
            _chunkCollision is null ||
            _fixture is null)
        {
            return;
        }

        if (_deferCollisionOneFrame)
        {
            _deferCollisionOneFrame = false;
            return;
        }

        if (_pendingCollisionRevision != _fixture.Chunk.Revision)
        {
            _pendingCollisionData = null;
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _chunkCollision.Shape =
            ChunkMeshBuilder.CreateCollisionShape(_pendingCollisionData);
        stopwatch.Stop();

        GD.Print(
            $"chunk.collision revision={_pendingCollisionRevision} " +
            $"publish_ms={stopwatch.Elapsed.TotalMilliseconds:F2}");

        _pendingCollisionData = null;
    }

    private sealed record ChunkPresentationBuild(
        ulong Revision,
        Chunk LightingSnapshot,
        ChunkMeshData MeshData,
        double WorkerMilliseconds);
}
