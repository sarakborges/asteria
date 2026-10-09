using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

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
    private readonly PlayerInventory _inventory;
    private readonly HeldVisualResolver _heldResolver;
    private HeldItemPresentation? _held;
    private ulong _lastInventoryRevision = ulong.MaxValue;
    private readonly Dictionary<string, (Node3D Pivot, Vector3 Position)> _pivots = [];
    private AnimationPlayer? _animations;
    private string? _currentClip;
    private float _crouchBlend;

    public PlayerModelPresentation(
        FpsPlayer player, PackSelection selection,
        PlayerVisualDefinition definition, PlayerInventory inventory,
        HeldVisualResolver heldResolver)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _selection = selection;
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _heldResolver = heldResolver ?? throw new ArgumentNullException(nameof(heldResolver));
        Name = "PlayerVisual";
        Visible = false;
    }

    public override void _Ready()
    {
        var imported = PlayerVisualSceneFactory.Load(_selection, _definition);
        var facing = new Node3D
        {
            Name = "PlayerModelFacing",
            Rotation = new Vector3(0f, Mathf.Pi, 0f),
        };
        AddChild(facing);
        facing.AddChild(imported);

        foreach (var child in PlayerVisualSceneFactory.Descendants(imported))
        {
            if (child is AnimationPlayer animation)
                _animations = animation;
            if (child is Node3D pivot && IsAnimatedPivot(pivot.Name.ToString()))
                _pivots.Add(pivot.Name.ToString(), (pivot, pivot.Position));
        }
        if (_pivots.TryGetValue("RightArmPivot", out var hand))
        {
            _held = new HeldItemPresentation(_selection);
            hand.Pivot.AddChild(_held);
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
        if (_held is not null && _lastInventoryRevision != _inventory.Revision)
        {
            _held.SetVisual(_heldResolver.Resolve(_inventory.SelectedStack));
            _lastInventoryRevision = _inventory.Revision;
        }
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

}
