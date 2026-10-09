using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Rendering;

/// <summary>
/// The local player's visual-only model. The published FpsPlayer remains the
/// sole owner of physical movement/posture. Assets are decoded from the pack
/// directly: no Godot imports, sidecars or ResourceLoader dependencies.
/// </summary>
public sealed partial class PlayerModelPresentation : Node3D
{
    private const float CrouchBlendSpeed = 8f;
    private const float MovingThresholdSquared = 0.01f;

    private readonly FpsPlayer _player;
    private readonly PackSelection _selection;
    private readonly PlayerVisualDefinition _definition;
    private readonly Dictionary<string, (Node3D Pivot, Vector3 Position)> _pivots = [];
    private AnimationPlayer? _animations;
    private string? _currentClip;
    private float _crouchBlend;

    public PlayerModelPresentation(
        FpsPlayer player, PackSelection selection,
        PlayerVisualDefinition definition)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _selection = selection;
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Name = "PlayerVisual";
        Visible = false;
    }

    public override void _Ready()
    {
        var source = ProjectPackFiles.AbsoluteResourcePath(
            _selection, _definition.Model);
        var bytes = File.ReadAllBytes(source);
        var state = new GltfState();
        var document = new GltfDocument();
        var status = document.AppendFromBuffer(bytes, "", state);
        if (status != Error.Ok)
            throw new InvalidDataException(
                $"Cannot decode authored player GLB: {status}");

        var imported = document.GenerateScene(state)
            ?? throw new InvalidDataException("Authored player GLB has no scene.");
        var facing = new Node3D
        {
            Name = "PlayerModelFacing",
            Rotation = new Vector3(0f, Mathf.Pi, 0f),
        };
        AddChild(facing);
        facing.AddChild(imported);

        var skin = ProjectPackFiles.LoadTexture(_selection, _definition.Skin);
        foreach (var child in Descendants(imported))
        {
            if (child is AnimationPlayer animation)
                _animations = animation;
            if (child is Node3D pivot && IsAnimatedPivot(pivot.Name.ToString()))
                _pivots.Add(pivot.Name.ToString(), (pivot, pivot.Position));
            if (child is MeshInstance3D mesh &&
                PlayerSkinUvMapper.TryMap(
                    mesh.Name.ToString(), NVector3.Zero,
                    NVector3.UnitZ, out _))
            {
                ApplySkin(mesh, skin);
            }
        }
        if (_animations is null)
            throw new InvalidDataException(
                "Authored player GLB is missing an AnimationPlayer.");

        foreach (var required in new[] { "idle", "walk", "run", "jump", "fall" })
            if (FindClip(_definition.Animations[required]) is null)
                throw new InvalidDataException(
                    $"Authored player GLB lacks animation '{required}'.");

        foreach (var key in new[] { "idle", "walk", "run", "fall" })
        {
            var name = FindClip(_definition.Animations[key]);
            if (name is not null)
                _animations.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        }
    }

    public override void _Process(double delta)
    {
        if (_animations is null) return;
        Visible = _player.IsThirdPerson &&
            !_player.PlayerState.GameMode.IsSpectator();
        if (!Visible) return;

        var velocity = _player.Velocity;
        var grounded = _player.IsOnFloor();
        var speedSquared = velocity.X * velocity.X + velocity.Z * velocity.Z;
        var state = !_player.PlayerState.IsFlying && !grounded
            ? velocity.Y > 0.05f ? "jump" : "fall"
            : speedSquared > MovingThresholdSquared
                ? _player.IsRunning ? "run" : "walk"
                : "idle";

        var clip = FindClip(_definition.Animations[state]);
        if (clip is not null &&
            (clip != _currentClip || !_animations.IsPlaying()))
        {
            _animations.Play(clip, customBlend: 0.08f);
            _currentClip = clip;
        }

        _crouchBlend = Mathf.MoveToward(_crouchBlend,
            _player.IsCrouching ? 1f : 0f,
            CrouchBlendSpeed * (float)delta);
        foreach (var (part, entry) in _pivots)
        {
            var drop = part switch
            {
                "BodyPivot" => 0.18f,
                "HeadPivot" => 0.28f,
                "RightArmPivot" or "LeftArmPivot" => 0.24f,
                "RightLegPivot" or "LeftLegPivot" => 0.02f,
                _ => 0f
            };
            entry.Pivot.Position = entry.Position -
                Vector3.Up * (drop * _crouchBlend);
        }
    }

    private string? FindClip(string wanted)
    {
        if (_animations is null) return null;
        foreach (var animation in _animations.GetAnimationList())
        {
            var name = animation.ToString();
            if (name == wanted ||
                name.EndsWith("/" + wanted, StringComparison.Ordinal))
                return name;
        }
        return null;
    }

    private static bool IsAnimatedPivot(string name) =>
        name is "BodyPivot" or "HeadPivot" or
            "RightArmPivot" or "LeftArmPivot" or
            "RightLegPivot" or "LeftLegPivot";

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static void ApplySkin(MeshInstance3D mesh, Texture2D skin)
    {
        if (mesh.Mesh is not Mesh imported)
            throw new InvalidDataException(
                $"Player mesh '{mesh.Name}' has no geometry.");

        var mapped = new ArrayMesh();
        for (var surface = 0; surface < imported.GetSurfaceCount(); surface++)
        {
            var arrays = imported.SurfaceGetArrays(surface);
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (positions.Length == 0 || positions.Length != normals.Length)
                throw new InvalidDataException(
                    $"Player skin geometry '{mesh.Name}' has invalid normals.");

            var uv = new Vector2[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                if (!PlayerSkinUvMapper.TryMap(
                        mesh.Name.ToString(),
                        new NVector3(positions[i].X, positions[i].Y, positions[i].Z),
                        new NVector3(normals[i].X, normals[i].Y, normals[i].Z),
                        out var mappedUv))
                    throw new InvalidDataException(
                        $"Cannot map player skin UV for '{mesh.Name}'.");
                uv[i] = new Vector2(mappedUv.X, mappedUv.Y);
            }
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
            mapped.AddSurfaceFromArrays(
                imported is ArrayMesh sourceMesh
                    ? sourceMesh.SurfaceGetPrimitiveType(surface)
                    : Mesh.PrimitiveType.Triangles,
                arrays);
        }

        mesh.Mesh = mapped;
        mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = skin,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }
}
