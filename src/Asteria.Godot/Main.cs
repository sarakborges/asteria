using System.Text.Json;
using System.Threading.Tasks;
using Asteria.Client.Rendering;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client;

public partial class Main : Node3D
{
    private readonly Vector3 _cameraTarget = new(0f, 10f, 0f);
    private Task<Chunk>? _generationTask;
    private Camera3D _camera = null!;
    private Node _webUi = null!;
    private double _elapsed;
    private bool _chunkAttached;
    private bool _generationErrorReported;

    public override void _Ready()
    {
        SetupLighting();
        SetupCamera();
        SetupWebUi();

        // Pure world work stays outside Godot's main thread.
        _generationTask = Task.Run(TestWorldGenerator.GenerateChunk);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        UpdateCamera();

        if (_chunkAttached || _generationTask is null)
        {
            return;
        }

        if (_generationTask.IsCompletedSuccessfully)
        {
            AttachChunk(_generationTask.Result);
            _chunkAttached = true;
            GD.Print("test-1: chunk generated and meshed successfully");
            SendWebUi("game.chunk_ready", new { size = Chunk.SizeX });
            return;
        }

        if (_generationTask.IsFaulted && !_generationErrorReported)
        {
            _generationErrorReported = true;
            GD.PushError(_generationTask.Exception?.ToString() ?? "Chunk generation failed.");
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
        SendWebUi("game.ready", new { bridge = 1, engine = "godot" });
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
                    SendWebUi("game.ready", new { bridge = 1, engine = "godot" });
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

    private void SendWebUi(string type, object payload)
    {
        _webUi.Call("post_message", JsonSerializer.Serialize(new { type, payload }));
    }

    private void AttachChunk(Chunk chunk)
    {
        var meshInstance = new MeshInstance3D
        {
            Name = "TestChunk",
            Mesh = ChunkMeshBuilder.Build(chunk),
            Position = new Vector3(-Chunk.SizeX / 2f, 0f, -Chunk.SizeZ / 2f),
        };

        AddChild(meshInstance);
    }

    private void SetupLighting()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55f, -35f, 0f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
        };

        AddChild(sun);
    }

    private void SetupCamera()
    {
        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Fov = 62f,
        };

        AddChild(_camera);
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var angle = (float)(_elapsed * 0.22);
        const float radius = 46f;
        _camera.Position = _cameraTarget + new Vector3(Mathf.Cos(angle) * radius, 20f, Mathf.Sin(angle) * radius);
        _camera.LookAt(_cameraTarget, Vector3.Up);
    }
}
