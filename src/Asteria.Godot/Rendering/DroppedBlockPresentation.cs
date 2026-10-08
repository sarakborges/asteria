using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class DroppedBlockPresentation
{
    private const float VisualScale = 0.36f;
    private const float SpinRadiansPerSecond = 0.9f;

    public DroppedBlockPresentation(
        DroppedBlockState state,
        ArrayMesh mesh)
        : this(state, new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
            Scale = Vector3.One * VisualScale,
            Position = -Vector3.One * (VisualScale * 0.5f),
        })
    {
    }

    public DroppedBlockPresentation(
        DroppedBlockState state,
        Texture2D icon)
        : this(state, new Sprite3D
        {
            Name = "Icon",
            Texture = icon ?? throw new ArgumentNullException(nameof(icon)),
            PixelSize = 0.006f,
        })
    {
    }

    private DroppedBlockPresentation(
        DroppedBlockState state,
        Node3D visual)
    {
        Id = state.Id;
        Root = new Node3D { Name = $"DroppedItem_{state.Id.Value}" };
        Root.AddChild(visual);
        Apply(state);
    }

    public DroppedBlockId Id { get; }

    public Node3D Root { get; }

    public void Apply(
        DroppedBlockState state)
    {
        Root.Position =
            new Vector3(
                state.Position.X,
                state.Position.Y,
                state.Position.Z);
        Root.Rotation =
            new Vector3(
                0f,
                (float)(
                    state.AgeSeconds *
                    SpinRadiansPerSecond),
                0f);
    }

    public void Retire()
    {
        Root.QueueFree();
    }
}
