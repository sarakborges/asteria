using Asteria.Core.World;
using Asteria.Core.Settings;
using Godot;

namespace Asteria.Client.Gameplay;

public partial class FpsPlayer : CharacterBody3D
{
    private const float MoveSpeed = 7.5f;
    private const float JumpSpeed = 8.0f;
    private const float FlySpeedMultiplier = 5.0f;
    private const float MouseSensitivity = 0.0022f;
    private const float MaxPitch = 1.52f;

    private Camera3D _camera = null!;
    private bool _mouseCaptured;
    private bool _moveForward;
    private bool _moveBackward;
    private bool _moveLeft;
    private bool _moveRight;
    private bool _jumpHeld;
    private bool _descendHeld;
    private bool _inputSuspended;
    private uint _solidCollisionLayer;
    private uint _solidCollisionMask;
    private bool _lastEyeSubmerged;
    private FluidRuntimeId _lastVisualFluid;
    private float _gravityStrength = 18f;

    public event Action? BreakRequested;
    public event Action? PlaceRequested;
    public event Action<bool>? MouseCaptureChanged;
    public event Action<FluidBodyContact>?
        FluidContactChanged;

    public PlayerSessionState PlayerState { get; set; } = null!;

    public ClientPreferences InputPreferences { get; set; } = null!;

    public Func<ulong> CurrentWorldTick { get; set; } = null!;

    public bool IsMouseCaptured => _mouseCaptured;

    public Camera3D Camera => _camera;

    public float GravityStrength
    {
        get => _gravityStrength;
        set
        {
            if (!float.IsFinite(value) ||
                value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Player gravity strength must be finite and non-negative.");
            }

            _gravityStrength = value;
        }
    }

    public Func<WorldAabb, float, FluidBodyContact>?
        FluidContactProvider { get; set; }

    public Func<FluidRuntimeId, FluidMotionDefinition>?
        FluidMotionProvider { get; set; }

    public override void _Ready()
    {
        ArgumentNullException.ThrowIfNull(PlayerState);
        ArgumentNullException.ThrowIfNull(InputPreferences);
        ArgumentNullException.ThrowIfNull(CurrentWorldTick);

        _solidCollisionLayer = CollisionLayer;
        _solidCollisionMask = CollisionMask;
        var collider = new CollisionShape3D
        {
            Name = "Collider",
            Position = new Vector3(0f, 0.9f, 0f),
            Shape = new CapsuleShape3D
            {
                Radius = 0.35f,
                Height = 1.8f,
            },
        };

        _camera = new Camera3D
        {
            Name = "Camera",
            Position = new Vector3(0f, 1.6f, 0f),
            Current = true,
            Fov = 75f,
        };

        AddChild(collider);
        AddChild(_camera);
        ApplyGameMode();
        CaptureMouse();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_inputSuspended)
        {
            return;
        }

        if (inputEvent is InputEventKey key)
        {
            HandleKey(key);
            return;
        }

        if (!_mouseCaptured)
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);
            _camera.Rotation = new Vector3(
                Mathf.Clamp(_camera.Rotation.X - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitch, MaxPitch),
                0f,
                0f);
            return;
        }

        if (inputEvent is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.Left:
                    BreakRequested?.Invoke();
                    break;
                case MouseButton.Right:
                    PlaceRequested?.Invoke();
                    break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;
        var movement = GetMovementInput();
        var direction = GlobalTransform.Basis *
            new Vector3(movement.X, 0f, movement.Y);
        direction.Y = 0f;
        direction = direction.Normalized();

        if (PlayerState.IsFlying)
        {
            var flySpeed = MoveSpeed * FlySpeedMultiplier;
            var vertical = (_jumpHeld ? 1f : 0f) -
                (_descendHeld ? 1f : 0f);
            Velocity = direction * flySpeed +
                Vector3.Up * (vertical * flySpeed);

            if (PlayerState.GameMode.IsSpectator())
            {
                GlobalPosition += Velocity * (float)delta;
                if (GlobalPosition.Y < 0f)
                {
                    GlobalPosition = new Vector3(
                        GlobalPosition.X, 0f, GlobalPosition.Z);
                }
                return;
            }

            MoveAndSlide();
            if (_descendHeld && IsOnFloor())
            {
                if (PlayerState.Land())
                {
                    Velocity = Vector3.Zero;
                }
            }
            return;
        }

        var fluidContact =
            FluidContactProvider?.Invoke(
                CollisionBounds,
                _camera.GlobalPosition.Y) ??
            default;

        PublishFluidContact(
            fluidContact);

        var fluidMotion =
            fluidContact.IsImmersed
                ? FluidMotionProvider?.Invoke(
                      fluidContact.Fluid) ??
                  FluidMotionDefinition.Default
                : default;

        if (fluidContact.IsImmersed)
        {
            velocity.Y =
                FluidMotionSolver.VerticalSpeed(
                    velocity.Y,
                    (float)delta,
                    fluidContact,
                    fluidMotion,
                    _jumpHeld);
        }
        else if (IsOnFloor())
        {
            if (_jumpHeld)
            {
                velocity.Y = JumpSpeed;
            }
        }
        else
        {
            velocity.Y -=
                GravityStrength *
                (float)delta;
        }

        if (fluidContact.IsImmersed)
        {
            var targetSpeed =
                MoveSpeed *
                fluidMotion.HorizontalSpeedMultiplier;
            var acceleration =
                fluidMotion.HorizontalAcceleration *
                (float)delta;

            velocity.X =
                Mathf.MoveToward(
                    velocity.X,
                    direction.X *
                    targetSpeed,
                    acceleration);
            velocity.Z =
                Mathf.MoveToward(
                    velocity.Z,
                    direction.Z *
                    targetSpeed,
                    acceleration);
        }
        else
        {
            velocity.X =
                direction.X *
                MoveSpeed;
            velocity.Z =
                direction.Z *
                MoveSpeed;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private Vector2 GetMovementInput()
    {
        var movement = Vector2.Zero;
        if (_moveForward) movement.Y -= 1f;
        if (_moveBackward) movement.Y += 1f;
        if (_moveLeft) movement.X -= 1f;
        if (_moveRight) movement.X += 1f;
        return movement.LimitLength(1f);
    }

    public void ApplyGameMode()
    {
        var spectator = PlayerState.GameMode.IsSpectator();
        CollisionLayer = spectator ? 0u : _solidCollisionLayer;
        CollisionMask = spectator ? 0u : _solidCollisionMask;
        Velocity = Vector3.Zero;
        ClearGameplayInput();
    }

    public void SuspendForKeyCapture()
    {
        _inputSuspended = true;
        ReleaseMouse();
        ClearGameplayInput();
    }

    public void ResumeAfterKeyCapture()
    {
        _inputSuspended = false;
        ClearGameplayInput();
    }

    public void ClearGameplayInput()
    {
        _moveForward = false;
        _moveBackward = false;
        _moveLeft = false;
        _moveRight = false;
        _jumpHeld = false;
        _descendHeld = false;
        PlayerState.CancelDoubleTap();
    }

    public (Vector3 From, Vector3 To) GetInteractionRay(float distance)
    {
        var from = _camera.GlobalPosition;
        var forward = -_camera.GlobalTransform.Basis.Z;
        return (from, from + forward * distance);
    }

    public WorldAabb CollisionBounds
    {
        get
        {
            const float halfWidth = 0.35f;
            const float height = 1.8f;

            var minimum =
                GlobalPosition +
                new Vector3(
                    -halfWidth,
                    0f,
                    -halfWidth);
            var maximum =
                GlobalPosition +
                new Vector3(
                    halfWidth,
                    height,
                    halfWidth);

            return new WorldAabb(
                new System.Numerics.Vector3(
                    minimum.X,
                    minimum.Y,
                    minimum.Z),
                new System.Numerics.Vector3(
                    maximum.X,
                    maximum.Y,
                    maximum.Z));
        }
    }

    public override void _ExitTree()
    {
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private void PublishFluidContact(
        FluidBodyContact contact)
    {
        var visualFluid =
            contact.EyeSubmerged
                ? contact.Fluid
                : FluidRuntimeId.None;

        if (contact.EyeSubmerged ==
                _lastEyeSubmerged &&
            visualFluid ==
                _lastVisualFluid)
        {
            return;
        }

        _lastEyeSubmerged =
            contact.EyeSubmerged;
        _lastVisualFluid =
            visualFluid;
        FluidContactChanged?.Invoke(
            contact);
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && key.Pressed && !key.Echo)
        {
            if (_mouseCaptured) ReleaseMouse();
            else CaptureMouse();
            return;
        }

        if (!_mouseCaptured)
        {
            return;
        }

        var code = key.PhysicalKeycode == Key.None
            ? key.Keycode : key.PhysicalKeycode;

        switch (code)
        {
            case Key.W:
                _moveForward = key.Pressed;
                break;
            case Key.S:
                _moveBackward = key.Pressed;
                break;
            case Key.A:
                _moveLeft = key.Pressed;
                break;
            case Key.D:
                _moveRight = key.Pressed;
                break;
        }

        if (GameplayKeyMap.Matches(
                key, InputPreferences, KeybindAction.Jump))
        {
            if (key.Pressed && !key.Echo && !_jumpHeld)
            {
                PlayerState.JumpPressed(CurrentWorldTick());
            }
            _jumpHeld = key.Pressed;
        }

        if (GameplayKeyMap.Matches(
                key, InputPreferences, KeybindAction.Descend))
        {
            _descendHeld = key.Pressed;
        }
    }

    private void CaptureMouse()
    {
        _mouseCaptured = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        MouseCaptureChanged?.Invoke(true);
    }

    private void ReleaseMouse()
    {
        _mouseCaptured = false;
        ClearGameplayInput();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        MouseCaptureChanged?.Invoke(false);
    }
}
