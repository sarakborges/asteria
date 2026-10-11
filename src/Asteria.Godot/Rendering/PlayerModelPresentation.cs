using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public enum PlayerVisualAction : byte
{
    Hit,
    Break,
    Place,
    Hurt,
    Death,
}

/// <summary>
/// The local player's visual-only model. The published FpsPlayer remains the
/// sole owner of physical movement/posture. Assets are decoded from the pack
/// directly: no Godot imports, sidecars or ResourceLoader dependencies.
/// </summary>
public sealed partial class PlayerModelPresentation : Node3D
{
    private const float CrouchBlendSpeed = 8f;
    private const float MovingStartThresholdSquared = 0.01f;
    private const float MovingStopThresholdSquared = 0.0025f;
    private const float CrouchHeightRatio = 1.5f / 1.8f;

    private readonly FpsPlayer _player;
    private readonly PackSelection _selection;
    private readonly PlayerVisualDefinition _definition;
    private readonly PlayerInventory _inventory;
    private readonly HeldVisualResolver _heldResolver;
    private readonly PackContentRegistry<ItemDefinition> _items;
    private readonly Func<Color?> _selectedToolTint;
    private Color? _lastToolTint;
    private PlayerEquipmentPresentation? _equipment;
    private HeldItemPresentation? _held;
    private PlayerFirstPersonViewModel? _firstPerson;
    private SubViewportContainer? _viewModelFrame;
    private Camera3D? _viewModelCamera;
    private Node3D? _facing;
    private bool _wasMoving;
    private ulong _lastInventoryRevision = ulong.MaxValue;
    private readonly Dictionary<string, (Node3D Pivot, Vector3 Position)> _pivots = [];
    private AnimationPlayer? _animations;
    private string? _currentClip;
    private float _crouchBlend;
    private float _actionRemaining;
    private bool _deathPlaying;
    private string? _actionClip;

    public PlayerModelPresentation(
        FpsPlayer player, PackSelection selection,
        PlayerVisualDefinition definition, PlayerInventory inventory,
        HeldVisualResolver heldResolver, PackContentRegistry<ItemDefinition> items,
        Func<Color?> selectedToolTint)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _selection = selection;
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _heldResolver = heldResolver ?? throw new ArgumentNullException(nameof(heldResolver));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _selectedToolTint = selectedToolTint ??
            throw new ArgumentNullException(nameof(selectedToolTint));
        Name = "PlayerVisual";
        Visible = false;
    }

    public override void _Ready()
    {
        var imported = PlayerVisualSceneFactory.Load(_selection, _definition);
        _facing = new Node3D
        {
            Name = "PlayerModelFacing",
            Rotation = new Vector3(0f, Mathf.Pi, 0f),
        };
        AddChild(_facing);
        _facing.AddChild(imported);
        _equipment = new PlayerEquipmentPresentation(imported, _inventory, _items);
        _equipment.Sync();

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

        // An isolated transparent 3D viewport gives the arm and held item
        // their own depth buffer, matching MineClone's overlay camera.
        // It avoids both terrain clipping and turning off depth testing on
        // the cuboids (which would expose their rear faces).
        var overlay = new CanvasLayer
        {
            Name = "PlayerViewModelOverlay",
            Layer = 1,
        };
        _player.AddChild(overlay);
        _viewModelFrame = new SubViewportContainer
        {
            Name = "ViewModelFrame",
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        overlay.AddChild(_viewModelFrame);
        _viewModelFrame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var viewPort = new SubViewport
        {
            Name = "ViewModelViewport",
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Size = new Vector2I(64, 64),
        };
        _viewModelFrame.AddChild(viewPort);
        _viewModelCamera = new Camera3D
        {
            Name = "ViewModelCamera",
            Current = true,
            Fov = _player.Camera.Fov,
            Near = 0.02f,
            Far = 10f,
        };
        viewPort.AddChild(_viewModelCamera);
        _firstPerson = new PlayerFirstPersonViewModel(
            _player, _selection, _definition, _inventory, _heldResolver);
        _viewModelCamera.AddChild(_firstPerson);

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

        var idle = FindClip(_definition.Animations["idle"]);
        if (idle is not null)
        {
            _animations.Play(idle);
            _currentClip = idle;
        }
    }

    /// <summary>
    /// Reacts to a native gameplay action; no input or combat state is owned
    /// here. One-shot actions interrupt locomotion without a rest-pose frame.
    /// </summary>
    public bool TryPlayAction(PlayerVisualAction action, bool fastBreak = false)
    {
        if (_deathPlaying) return false;
        var firstPersonPlayed = _firstPerson?.TryPlayAction(action, fastBreak) ?? false;
        if (_animations is null) return firstPersonPlayed;

        var key = action switch
        {
            PlayerVisualAction.Hit => "hit",
            PlayerVisualAction.Break => "break",
            PlayerVisualAction.Place => "place",
            PlayerVisualAction.Hurt => "hurt",
            PlayerVisualAction.Death => "death",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        if (!_definition.Animations.TryGetValue(key, out var authored) ||
            FindClip(authored) is not { } clip)
            return false;

        if (action is not PlayerVisualAction.Death &&
            action is not PlayerVisualAction.Hurt && _actionClip is not null &&
            _actionRemaining > 0f && _actionClip == "hurt")
            return false;

        _animations.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.None;
        var speed = action == PlayerVisualAction.Break && fastBreak
            ? 22f / 8f : 1f;
        _animations.Play(clip, customBlend: action is PlayerVisualAction.Hurt
            ? 0.08f : 0f, customSpeed: speed);
        _currentClip = clip;
        _actionClip = key;
        _actionRemaining = Mathf.Clamp(
            (float)_animations.GetAnimation(clip).Length / speed, 0.12f, 2f);
        _deathPlaying = action == PlayerVisualAction.Death;
        return true;
    }

    public override void _Process(double delta)
    {
        if (_animations is null) return;
        _equipment?.Sync();
        var toolTint = _selectedToolTint();
        if (_lastToolTint != toolTint)
        {
            _lastToolTint = toolTint;
            _held?.SetTint(toolTint);
            _firstPerson?.SetToolTint(toolTint);
        }
        if (_held is not null && _lastInventoryRevision != _inventory.Revision)
        {
            _held.SetVisual(_heldResolver.Resolve(_inventory.SelectedStack));
            _lastInventoryRevision = _inventory.Revision;
        }
        Visible = _player.IsThirdPerson &&
            !_player.PlayerState.GameMode.IsSpectator();
        if (_viewModelFrame is not null)
            _viewModelFrame.Visible = !_player.IsThirdPerson &&
                !_player.PlayerState.GameMode.IsSpectator() &&
                !_player.PlayerState.Health.IsDead &&
                _player.IsMouseCaptured;
        if (_viewModelCamera is not null)
            _viewModelCamera.Fov = _player.Camera.Fov;
        if (_actionRemaining > 0f)
            _actionRemaining = Mathf.Max(0f, _actionRemaining - (float)delta);
        if (_actionRemaining <= 0f && !_deathPlaying)
            _actionClip = null;

        if (!Visible) return;

        var velocity = _player.Velocity;
        var grounded = _player.IsOnFloor();
        var speedSquared = velocity.X * velocity.X + velocity.Z * velocity.Z;
        var moving = speedSquared > (_wasMoving
            ? MovingStopThresholdSquared : MovingStartThresholdSquared);
        _wasMoving = moving;
        var state = !_player.PlayerState.IsFlying && !grounded
            ? velocity.Y > 0.05f ? "jump" : "fall"
            : moving ? _player.IsRunning ? "run" : "walk" : "idle";

        var clip = FindClip(_definition.Animations[state]);
        if (!_deathPlaying && _actionClip is null && clip is not null &&
            (clip != _currentClip || !_animations.IsPlaying()))
        {
            _animations.Play(clip, customBlend: 0.08f);
            _currentClip = clip;
        }

        _crouchBlend = Mathf.MoveToward(_crouchBlend,
            _player.IsCrouching ? 1f : 0f,
            CrouchBlendSpeed * (float)delta);
        // Crouch the assembled figure as one unit. Translating individual
        // pivots at different speeds detached the head, torso and limbs.
        if (_facing is not null)
            _facing.Scale = new Vector3(
                1f, Mathf.Lerp(1f, CrouchHeightRatio, _crouchBlend), 1f);
        if (_pivots.TryGetValue("HeadPivot", out var head))
            head.Pivot.Rotation = new Vector3(-_player.CameraPitch, 0f, 0f);
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
