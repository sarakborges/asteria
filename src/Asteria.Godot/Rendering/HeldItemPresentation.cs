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
    private const float CubeSize = PlayerHeldVisualLayout.ThirdPersonBlockSize;
    private const float SpriteSize = PlayerHeldVisualLayout.SpriteSize;
    private const float ToolAngleRadians = PlayerHeldVisualLayout.ToolDisplayAngle;
    private const float ToolGripFraction = -0.36f;
    private const float TintDepth = 0.004f;
    private readonly PackSelection _selection;
    private readonly WorldObjectVisualCatalog _objects;
    private readonly bool _firstPerson;
    private HeldVisual? _selected;
    private Color? _tint;
    private MeshInstance3D? _tintLayer;
    private StandardMaterial3D? _tintMaterial;

    public HeldItemPresentation(
        PackSelection selection, WorldObjectVisualCatalog objects,
        bool firstPerson = false)
    {
        _selection = selection;
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _firstPerson = firstPerson;
        Name = "HeldItem";
        if (!firstPerson)
        {
            var position = PlayerHeldVisualLayout.ThirdPersonHandPosition;
            var angles = PlayerHeldVisualLayout.ThirdPersonHandEulerXyz;
            Position = new Vector3(position.X, position.Y, position.Z);
            Quaternion = PlayerVisualRotation.EulerXyz(
                new Vector3(angles.X, angles.Y, angles.Z));
        }
    }

    public void SetVisual(HeldVisual? visual)
    {
        if (Equals(_selected, visual)) return;
        _selected = visual;
        if (_firstPerson)
        {
            // Align with MineClone's distinct camera-space block/sprite grips.
            var anchor = PlayerHeldVisualLayout.FirstPersonHandPosition(visual?.Kind);
            Position = new Vector3(anchor.X, anchor.Y, anchor.Z);
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

        if (visual.Kind == HeldVisualKind.Object)
        {
            if (visual.Object is null)
                throw new InvalidDataException(
                    "A held Object requires its authored visual definition.");
            AddObjectMesh(visual.Object);
        }
        else if (visual.Kind == HeldVisualKind.Cube)
        {
            var size = _firstPerson ? PlayerHeldVisualLayout.FirstPersonBlockSize : CubeSize;
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
                new Vector3(0f, 0f,
                    PlayerHeldVisualLayout.SpriteFaceAngle(visual.Kind)),
                SpriteSize);
        }
    }

    private void AddObjectMesh(ObjectDefinition definition)
    {
        var pose = PlayerHeldVisualLayout.ObjectPose(definition);
        var rotationRoot = new Node3D { Name = "ObjectDisplay" };
        if (pose.FaceCamera)
            rotationRoot.Rotation = new Vector3(Mathf.Pi * .5f, 0f, 0f);
        AddChild(rotationRoot);

        var offset = pose.CenterTranslation;
        var color = definition.Tint == Asteria.Core.World.BlockTint.None
            ? Colors.White
            : new Color(
                definition.TintFallback.Red / 255f,
                definition.TintFallback.Green / 255f,
                definition.TintFallback.Blue / 255f);
        rotationRoot.AddChild(new MeshInstance3D
        {
            Name = "ObjectMesh",
            Mesh = _objects.GetMesh(definition),
            Position = new Vector3(offset.X, offset.Y, offset.Z),
            Scale = Vector3.One * pose.Scale,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ProjectPackFiles.LoadTexture(
                    _selection, definition.Visual.Texture),
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = definition.Visual.AlphaCutoff,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }
        });
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
