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
    private const float ToolAngleRadians = 0.30f;
    private const float ToolGripFraction = -0.36f;
    private const float TintDepth = 0.004f;
    private readonly PackSelection _selection;
    private readonly bool _firstPerson;
    private HeldVisual? _selected;
    private Color? _tint;
    private MeshInstance3D? _tintLayer;
    private StandardMaterial3D? _tintMaterial;

    public HeldItemPresentation(PackSelection selection, bool firstPerson = false)
    {
        _selection = selection;
        _firstPerson = firstPerson;
        Name = "HeldItem";
        if (!firstPerson)
        {
            Position = new Vector3(0f, -0.72f, -0.06f);
            Rotation = new Vector3(-0.35f, 0.65f, 0.18f);
        }
    }

    public void SetVisual(HeldVisual? visual)
    {
        if (Equals(_selected, visual)) return;
        _selected = visual;
        if (_firstPerson)
        {
            // Align with MineClone's distinct camera-space block/sprite grips.
            Position = visual?.Kind switch
            {
                HeldVisualKind.Cube => new Vector3(-0.02f, 0.715f, 0.20f),
                HeldVisualKind.GrippedSprite => new Vector3(-0.08f, 0.52f, 0.21f),
                _ => new Vector3(0.04f, 0.76f, 0.21f),
            };
            Quaternion = PlayerFirstPersonViewModel.RestPoseRotation.Inverse();
        }

        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _tintLayer = null;
        _tintMaterial = null;
        if (visual is null) return;

        if (visual.Kind == HeldVisualKind.Cube)
        {
            var size = _firstPerson ? 0.18f : CubeSize;
            var h = size * 0.5f;
            AddFace("front", visual.FrontTexture,
                new Vector3(0, 0, h), Vector3.Zero, size);
            AddFace("back", visual.BackTexture ?? visual.FrontTexture,
                new Vector3(0, 0, -h), new Vector3(0, Mathf.Pi, 0), size);
            AddFace("right", visual.RightTexture ?? visual.FrontTexture,
                new Vector3(h, 0, 0), new Vector3(0, Mathf.Pi * 0.5f, 0), size);
            AddFace("left", visual.LeftTexture ?? visual.FrontTexture,
                new Vector3(-h, 0, 0), new Vector3(0, -Mathf.Pi * 0.5f, 0), size);
            AddFace("top", visual.TopTexture ?? visual.FrontTexture,
                new Vector3(0, h, 0), new Vector3(-Mathf.Pi * 0.5f, 0, 0), size);
            AddFace("bottom", visual.BottomTexture ?? visual.FrontTexture,
                new Vector3(0, -h, 0), new Vector3(Mathf.Pi * 0.5f, 0, 0), size);
        }
        else if (_firstPerson && visual.Kind == HeldVisualKind.GrippedSprite)
        {
            // Match MineClone's handle anchor instead of centering the icon.
            // Both sprite layers share rotation/position; depth offsets the
            // translucent tint just enough to avoid z-fighting the mask.
            var grip = new Vector2(ToolGripFraction, ToolGripFraction)
                .Rotated(ToolAngleRadians) * SpriteSize;
            var position = new Vector3(-grip.X, -grip.Y, 0f);
            var rotation = new Vector3(0f, 0f, ToolAngleRadians);
            AddFace("tool_base", visual.FrontTexture, position, rotation, SpriteSize);
            if (visual.TintTexture is { } tintTexture)
            {
                _tintLayer = AddFace("tool_tint", tintTexture,
                    position + new Vector3(0f, 0f, TintDepth), rotation,
                    SpriteSize, blended: true);
                _tintMaterial = (StandardMaterial3D)_tintLayer.MaterialOverride;
                ApplyTint();
            }
        }
        else
        {
            AddFace("sprite", visual.FrontTexture, Vector3.Zero,
                new Vector3(0, 0, Mathf.Pi * 0.30f), SpriteSize);
        }
    }

    /// <summary>Presentation-only palette color. Brush selection remains
    /// authoritative in ToolGameplayRuntime, not in this node.</summary>
    public void SetTint(Color? tint)
    {
        if (_tint == tint) return;
        _tint = tint;
        ApplyTint();
    }

    private void ApplyTint()
    {
        if (_tintLayer is null || _tintMaterial is null) return;
        _tintLayer.Visible = _tint.HasValue;
        if (_tint is { } color)
            _tintMaterial.AlbedoColor = color;
    }

    private MeshInstance3D AddFace(
        string name, string texturePath,
        Vector3 position, Vector3 rotation, float size,
        bool blended = false)
    {
        var instance = new MeshInstance3D
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
                Transparency = blended
                    ? BaseMaterial3D.TransparencyEnum.Alpha
                    : BaseMaterial3D.TransparencyEnum.AlphaScissor,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }
        };
        AddChild(instance);
        return instance;
    }
}
