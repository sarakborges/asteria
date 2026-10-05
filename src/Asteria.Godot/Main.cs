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
    private double _elapsed;
    private bool _chunkAttached;
    private bool _generationErrorReported;

    public override void _Ready()
    {
        SetupLighting();
        SetupCamera();

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
            return;
        }

        if (_generationTask.IsFaulted && !_generationErrorReported)
        {
            _generationErrorReported = true;
            GD.PushError(_generationTask.Exception?.ToString() ?? "Chunk generation failed.");
        }
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
