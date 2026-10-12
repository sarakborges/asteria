using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class FallingBlockPresentation
{
    private Vector3 _lastPosition;
    private bool _hasPosition;
    public FallingBlockPresentation(
        FallingBlockState state,
        ArrayMesh mesh)
    {
        Id = state.Id;

        Root = new Node3D
        {
            Name =
                $"FallingBlock_{state.Id.Value}",
        };

        Root.AddChild(
            new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = mesh,
            });

        Apply(state);
    }

    public FallingBlockId Id { get; }

    public Node3D Root { get; }

    public void Apply(
        FallingBlockState state)
    {
        var position = new Vector3(
            state.ColumnX,
            (float)(state.CenterY - 0.5),
            state.ColumnZ);
        // Sleeping blocks can remain unchanged for thousands of frames.
        // A Node3D position setter crosses into the engine and invalidates
        // transforms even when the coordinates have not moved.
        if (_hasPosition && _lastPosition == position)
            return;

        Root.Position = position;
        _lastPosition = position;
        _hasPosition = true;
    }

    public void Retire()
    {
        Root.QueueFree();
    }


}
