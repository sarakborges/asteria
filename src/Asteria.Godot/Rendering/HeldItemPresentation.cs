using Asteria.Client.Content;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Lightweight hand-held representation. Rebuilt only when the selected
/// authored visual changes; no per-item or per-block world node is created.
/// </summary>
internal sealed partial class HeldItemPresentation : Node3D
{
    private const float CubeSize = 0.22f;
    private const float SpriteSize = 0.52f;
    private readonly PackSelection _selection;
    private HeldVisual? _selected;

    public HeldItemPresentation(PackSelection selection)
    {
        _selection = selection;
        Name = "HeldItem";
        Position = new Vector3(0f, -0.72f, -0.06f);
        Rotation = new Vector3(-0.35f, 0.65f, 0.18f);
    }

    public void SetVisual(HeldVisual? visual)
    {
        if (Equals(_selected, visual)) return;
        _selected = visual;

        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        if (visual is null) return;

        if (visual.Kind == HeldVisualKind.Cube)
        {
            var h = CubeSize * 0.5f;
            AddFace("front", visual.FrontTexture,
                new Vector3(0, 0, h), Vector3.Zero, CubeSize);
            AddFace("back", visual.FrontTexture,
                new Vector3(0, 0, -h), new Vector3(0, Mathf.Pi, 0), CubeSize);
            AddFace("right", visual.FrontTexture,
                new Vector3(h, 0, 0), new Vector3(0, Mathf.Pi * 0.5f, 0), CubeSize);
            AddFace("left", visual.FrontTexture,
                new Vector3(-h, 0, 0), new Vector3(0, -Mathf.Pi * 0.5f, 0), CubeSize);
            AddFace("top", visual.TopTexture ?? visual.FrontTexture,
                new Vector3(0, h, 0), new Vector3(-Mathf.Pi * 0.5f, 0, 0), CubeSize);
            AddFace("bottom", visual.BottomTexture ?? visual.FrontTexture,
                new Vector3(0, -h, 0), new Vector3(Mathf.Pi * 0.5f, 0, 0), CubeSize);
        }
        else
        {
            AddFace("sprite", visual.FrontTexture, Vector3.Zero,
                new Vector3(0, 0, Mathf.Pi * 0.30f), SpriteSize);
        }
    }

    private void AddFace(
        string name, string texturePath,
        Vector3 position, Vector3 rotation, float size)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new QuadMesh { Size = new Vector2(size, size) },
            Position = position,
            Rotation = rotation,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ProjectPackFiles.LoadTexture(
                    _selection, texturePath),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }
        });
    }
}
