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
    private Task<TestChunkFixture>? _fixtureTask;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private bool _chunkAttached;
    private bool _fixtureErrorReported;

    public override void _Ready()
    {
        SetupLighting();
        SetupWebUi();

        var blocks = BlockContentLoader.LoadProjectBlocks();
        GD.Print($"block content: loaded {blocks.AuthoredCount} definitions");

        // Temporary QA fixture only. This is not a world-generation path.
        _fixtureTask = Task.Run(() => TestChunkFactory.Create(blocks));
    }

    public override void _Process(double delta)
    {
        if (_chunkAttached || _fixtureTask is null)
        {
            return;
        }

        if (_fixtureTask.IsCompletedSuccessfully)
        {
            var fixture = _fixtureTask.Result;
            AttachChunk(fixture.Chunk, fixture.Blocks);
            SetupPlayer();
            _chunkAttached = true;

            GD.Print($"test-4: data-driven blocks + 16^3 palette chunk ready; voxels={fixture.Chunk.NonEmptyVoxelCount}, palette={fixture.Chunk.PaletteEntryCount}");
            SendWebUi("game.chunk_ready", new
            {
                size = Chunk.Size,
                voxels = fixture.Chunk.NonEmptyVoxelCount,
                paletteEntries = fixture.Chunk.PaletteEntryCount,
                blocks = fixture.Blocks.AuthoredCount,
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

        if (_chunkAttached && _fixtureTask?.IsCompletedSuccessfully == true)
        {
            var fixture = _fixtureTask.Result;
            SendWebUi("game.chunk_ready", new
            {
                size = Chunk.Size,
                voxels = fixture.Chunk.NonEmptyVoxelCount,
                paletteEntries = fixture.Chunk.PaletteEntryCount,
                blocks = fixture.Blocks.AuthoredCount,
            });
        }

        if (_player is not null)
        {
            SendWebUi("game.player_ready", new { controller = "fps" });
        }
    }

    private void SendWebUi(string type, object payload)
    {
        _webUi.Call("post_message", JsonSerializer.Serialize(new { type, payload }));
    }

    private void AttachChunk(Chunk chunk, BlockRegistry blocks)
    {
        var mesh = ChunkMeshBuilder.Build(chunk, blocks);
        var chunkRoot = new Node3D
        {
            Name = "TestChunk",
            Position = new Vector3(-Chunk.Size / 2f, 0f, -Chunk.Size / 2f),
        };

        var meshInstance = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
        };

        var staticBody = new StaticBody3D
        {
            Name = "Collision",
        };

        var collisionShape = new CollisionShape3D
        {
            Name = "Shape",
            Shape = mesh.CreateTrimeshShape(),
        };

        staticBody.AddChild(collisionShape);
        chunkRoot.AddChild(meshInstance);
        chunkRoot.AddChild(staticBody);
        AddChild(chunkRoot);
    }

    private void SetupPlayer()
    {
        _player = new FpsPlayer
        {
            Name = "Player",
            Position = new Vector3(0f, 20f, 0f),
        };

        AddChild(_player);
    }

    private void SetupLighting()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55f, -35f, 0f),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
        };

        var fill = new DirectionalLight3D
        {
            Name = "FillLight",
            RotationDegrees = new Vector3(-35f, 145f, 0f),
            LightEnergy = 0.55f,
            ShadowEnabled = false,
        };

        AddChild(sun);
        AddChild(fill);
    }
}
