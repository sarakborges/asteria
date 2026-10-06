using System.Text.Json;
using System.Threading.Tasks;
using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Client.Rendering;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client;

public partial class Main : Node3D
{
    private const float InteractionDistance = 6f;
    private const float HitEpsilon = 0.001f;

    private Task<TestChunkFixture>? _fixtureTask;
    private TestChunkFixture? _fixture;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private ShaderMaterial _terrainMaterial = null!;
    private Node3D? _chunkRoot;
    private MeshInstance3D? _chunkMesh;
    private CollisionShape3D? _chunkCollision;
    private BlockRuntimeId _placementBlock;
    private bool _chunkAttached;
    private bool _chunkDirty;
    private bool _fixtureErrorReported;

    public override void _Ready()
    {
        SetupWebUi();

        var blocks = BlockContentLoader.LoadProjectBlocks();
        _terrainTextures = TerrainTextureCatalog.Create(blocks);
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

        if (_chunkDirty)
        {
            RebuildChunkPresentation();
        }
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
            AttachChunk(_fixture.Chunk, _fixture.Blocks);
            SetupPlayer();
            _chunkAttached = true;

            GD.Print(
                $"test-7: interactive voxel runtime ready; " +
                $"voxels={_fixture.Chunk.NonEmptyVoxelCount}, " +
                $"palette={_fixture.Chunk.PaletteEntryCount}");
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
        SendWebUi("game.mouse_capture", new { captured });
    }

    private void AttachChunk(Chunk chunk, BlockRegistry blocks)
    {
        var mesh = ChunkMeshBuilder.Build(
            chunk,
            blocks,
            _terrainTextures,
            _terrainMaterial);

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
            Shape = mesh.CreateTrimeshShape(),
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
            _chunkDirty = true;
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

        if (chunk.SetBlock(target.X, target.Y, target.Z, _placementBlock))
        {
            _chunkDirty = true;
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

        var (from, to) = _player.GetInteractionRay(InteractionDistance);
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;

        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return false;
        }

        var worldPosition = result["position"].AsVector3();
        var worldNormal = result["normal"].AsVector3();
        var localPosition = _chunkRoot.ToLocal(worldPosition);
        var localNormal = (_chunkRoot.GlobalTransform.Basis.Inverse() * worldNormal).Normalized();

        hitVoxel = FloorVoxel(localPosition - localNormal * HitEpsilon);
        if (!Chunk.Contains(hitVoxel.X, hitVoxel.Y, hitVoxel.Z))
        {
            return false;
        }

        faceOffset = DominantAxis(localNormal);
        return faceOffset != Vector3I.Zero;
    }

    private void RebuildChunkPresentation()
    {
        if (_fixture is null || _chunkMesh is null || _chunkCollision is null)
        {
            _chunkDirty = false;
            return;
        }

        ChunkLightingSolver.Initialize(_fixture.Chunk, _fixture.Blocks);

        var mesh = ChunkMeshBuilder.Build(
            _fixture.Chunk,
            _fixture.Blocks,
            _terrainTextures,
            _terrainMaterial);

        _chunkMesh.Mesh = mesh;
        _chunkCollision.Shape = mesh.CreateTrimeshShape();
        _chunkDirty = false;

        GD.Print(
            $"chunk.edit revision={_fixture.Chunk.Revision} " +
            $"voxels={_fixture.Chunk.NonEmptyVoxelCount}");
    }

    private static Vector3I FloorVoxel(Vector3 point) =>
        new(
            Mathf.FloorToInt(point.X),
            Mathf.FloorToInt(point.Y),
            Mathf.FloorToInt(point.Z));

    private static Vector3I DominantAxis(Vector3 normal)
    {
        var abs = normal.Abs();

        if (abs.X >= abs.Y && abs.X >= abs.Z)
        {
            return new Vector3I(Math.Sign(normal.X), 0, 0);
        }

        if (abs.Y >= abs.X && abs.Y >= abs.Z)
        {
            return new Vector3I(0, Math.Sign(normal.Y), 0);
        }

        return new Vector3I(0, 0, Math.Sign(normal.Z));
    }
}
