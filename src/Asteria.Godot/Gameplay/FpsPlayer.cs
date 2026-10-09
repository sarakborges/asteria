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
    private const float ThirdPersonCameraDistance = 4f;
    private const float StandingHeight = 1.8f;
    private const float CrouchHeight = 1.5f;
    private const float StandingEyeHeight = 1.6f;
    private const float CrouchEyeDrop = 0.35f;
    private const float CrouchCameraBlendSpeed = 8f;
    private const float WalkAcceleration = 28f;
    private const float WalkDeceleration = 36f;
    private const float CrouchEdgeProbe = 0.12f;

    private enum CameraView : byte
    {
        FirstPerson,
        ThirdPersonBack,
        ThirdPersonFront,
    }

    private Camera3D _camera = null!;
    private Node3D _cameraPivot = null!;
    private SpringArm3D _cameraArm = null!;
    private float _cameraPitch;
    private CameraView _cameraView;
    private CollisionShape3D _collider = null!;
    private CapsuleShape3D _standingCapsule = null!;
    private CapsuleShape3D _crouchedCapsule = null!;
    private readonly PlayerGroundMovement _groundMovement = new();
    private bool _mouseCaptured;
    private bool _moveForward;
    private bool _moveBackward;
    private bool _moveLeft;
    private bool _moveRight;
    private bool _breakHeld;
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
    public event Action? ToolActionRequested;
    public event Action? DropItemRequested;
    public event Action<int>? HotbarSlotRequested;
    public event Action? FlightStateChanged;
    public event Action<bool>? MouseCaptureChanged;
    public event Action<FluidBodyContact>?
        FluidContactChanged;

    public PlayerSessionState PlayerState { get; set; } = null!;

    public ClientPreferences InputPreferences { get; set; } = null!;

    public Func<ulong> CurrentWorldTick { get; set; } = null!;

    public bool IsMouseCaptured => _mouseCaptured;
    public bool IsBreakHeld => _breakHeld && _mouseCaptured && !_inputSuspended;
    public bool IsRunning => _groundMovement.IsRunning;
    public bool IsCrouching => _groundMovement.IsCrouching;

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
        _standingCapsule = new CapsuleShape3D
        {
            Radius = 0.35f,
            Height = StandingHeight,
        };
        _crouchedCapsule = new CapsuleShape3D
        {
            Radius = 0.35f,
            Height = CrouchHeight,
        };
        _collider = new CollisionShape3D
        {
            Name = "Collider",
            Position = new Vector3(0f, StandingHeight * 0.5f, 0f),
            Shape = _standingCapsule,
        };

        _cameraPivot = new Node3D
        {
            Name = "CameraPivot",
            Position = new Vector3(0f, StandingEyeHeight, 0f),
        };
        _cameraArm = new SpringArm3D
        {
            Name = "CameraCollisionArm",
            SpringLength = 0f,
            Margin = 0.12f,
            CollisionMask = _solidCollisionMask,
        };
        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Fov = 75f,
        };

        AddChild(_collider);
        AddChild(_cameraPivot);
        _cameraPivot.AddChild(_cameraArm);
        _cameraArm.AddChild(_camera);
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
            _cameraPitch = Mathf.Clamp(
                _cameraPitch - mouseMotion.Relative.Y * MouseSensitivity,
                -MaxPitch, MaxPitch);
            ApplyCameraView();
            return;
        }

        if (inputEvent is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
                _breakHeld = mouseButton.Pressed;
            if (!mouseButton.Pressed) return;
            if (mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                HotbarSlotRequested?.Invoke(
                    mouseButton.ButtonIndex == MouseButton.WheelUp ? -1 : -2);
                return;
            }
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
            UpdateGroundPosture(immersed: false, (float)delta);
            _groundMovement.CancelRunning();
        }
        else
        {
            // Use the published world contact, never a browser-side
            // swimming/crouching approximation.
            var contact = FluidContactProvider?.Invoke(
                CollisionBounds, _cameraPivot.GlobalPosition.Y) ?? default;
            UpdateGroundPosture(contact.IsImmersed, (float)delta);
        }

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
                    FlightStateChanged?.Invoke();
                }
            }
            return;
        }

        var fluidContact =
            FluidContactProvider?.Invoke(
                CollisionBounds,
                _cameraPivot.GlobalPosition.Y) ??
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
            var targetSpeed = MoveSpeed *
                _groundMovement.SpeedMultiplier(
                    flying: false, immersed: false);
            var target = new Vector2(direction.X, direction.Z) * targetSpeed;
            var horizontal = new Vector2(velocity.X, velocity.Z);
            var acceleration = (target == Vector2.Zero
                ? WalkDeceleration : WalkAcceleration) * (float)delta;
            horizontal = horizontal.MoveToward(target, acceleration);
            velocity.X = horizontal.X;
            velocity.Z = horizontal.Y;

            if (_groundMovement.IsCrouching && IsOnFloor())
            {
                // Sneaking must not move the full capsule past the
                // supported ledge. Check each horizontal axis against
                // Godot's real published terrain collision.
                var xMotion = new Vector3(velocity.X * (float)delta, 0f, 0f);
                if (xMotion.X != 0f && !HasGroundSupportAt(xMotion))
                    velocity.X = 0f;
                var zMotion = new Vector3(0f, 0f, velocity.Z * (float)delta);
                if (zMotion.Z != 0f && !HasGroundSupportAt(zMotion))
                    velocity.Z = 0f;
            }
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void UpdateGroundPosture(bool immersed, float delta)
    {
        var requested = _descendHeld && !immersed && !PlayerState.IsFlying;
        var canStand = !_groundMovement.IsCrouching || CanStandAtCurrentPosition();
        if (_groundMovement.UpdateCrouch(requested, canStand))
        {
            var height = _groundMovement.IsCrouching
                ? CrouchHeight : StandingHeight;
            _collider.Shape = _groundMovement.IsCrouching
                ? _crouchedCapsule : _standingCapsule;
            _collider.Position = new Vector3(0f, height * 0.5f, 0f);
        }

        var targetEyeY = StandingEyeHeight -
            (_groundMovement.IsCrouching ? CrouchEyeDrop : 0f);
        _cameraPivot.Position = new Vector3(0f,
            Mathf.MoveToward(_cameraPivot.Position.Y, targetEyeY,
                CrouchEyeDrop * CrouchCameraBlendSpeed * delta), 0f);
    }

    private bool CanStandAtCurrentPosition()
    {
        if (PlayerState.GameMode.IsSpectator())
            return true;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _standingCapsule,
            Transform = new Transform3D(
                _collider.GlobalTransform.Basis,
                GlobalPosition + Vector3.Up * (StandingHeight * 0.5f)),
            CollisionMask = _solidCollisionMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    private bool HasGroundSupportAt(Vector3 horizontalOffset) =>
        TestMove(GlobalTransform.Translated(horizontalOffset),
            Vector3.Down * CrouchEdgeProbe);

    private Vector2 GetMovementInput()
    {
        var movement = Vector2.Zero;
        if (_moveForward) movement.Y -= 1f;
        if (_moveBackward) movement.Y += 1f;
        if (_moveLeft) movement.X -= 1f;
        if (_moveRight) movement.X += 1f;
        return movement.LimitLength(1f);
    }

    /// <summary>
    /// Checks the actual published Godot collision shapes before leaving
    /// spectator. The caller also ensures the full body volume is resident.
    /// </summary>
    public bool CanEnterSolidMode()
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _collider.Shape,
            Transform = _collider.GlobalTransform,
            CollisionMask = _solidCollisionMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    public void ApplyGameMode()
    {
        var spectator = PlayerState.GameMode.IsSpectator();
        CollisionLayer = spectator ? 0u : _solidCollisionLayer;
        CollisionMask = spectator ? 0u : _solidCollisionMask;
        Velocity = Vector3.Zero;
        ClearGameplayInput();
    }

    public void SuspendForModal() => SuspendForKeyCapture();

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
        _breakHeld = false;
        _moveForward = false;
        _moveBackward = false;
        _moveLeft = false;
        _moveRight = false;
        _jumpHeld = false;
        _descendHeld = false;
        // Reset ground input so a paused/released key cannot leave
        // phantom running/crouching when the mouse is captured again.
        _groundMovement.Reset();
        if (_collider is not null && _standingCapsule is not null)
        {
            // Modal changes stop movement but must not expand a body
            // underneath a ceiling. Posture is resolved by physics on resume.
            if (CanStandAtCurrentPosition())
            {
                _collider.Shape = _standingCapsule;
                _collider.Position = new Vector3(0f, StandingHeight * 0.5f, 0f);
            }
        }
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
            var height = _groundMovement.IsCrouching
                ? CrouchHeight : StandingHeight;

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
            // Escape in a released-pointer UI must navigate the active
            // overlay; never capture the mouse behind settings/inventory.
            if (_mouseCaptured) ReleaseMouse();
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
                if (!key.Echo)
                    _groundMovement.ForwardChanged(
                        key.Pressed, CurrentWorldTick());
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

        if (key.Pressed && !key.Echo)
        {
            if (code is >= Key.Key1 and <= Key.Key9)
                HotbarSlotRequested?.Invoke((int)code - (int)Key.Key1);

            if (GameplayKeyMap.Matches(
                    key, InputPreferences, KeybindAction.DropItem))
                DropItemRequested?.Invoke();
        }

        if (GameplayKeyMap.Matches(
                key, InputPreferences, KeybindAction.Jump))
        {
            if (key.Pressed && !key.Echo && !_jumpHeld)
            {
                if (PlayerState.JumpPressed(CurrentWorldTick()))
                {
                    FlightStateChanged?.Invoke();
                }
            }
            _jumpHeld = key.Pressed;
        }

        if (GameplayKeyMap.Matches(
                key, InputPreferences, KeybindAction.Descend))
        {
            _descendHeld = key.Pressed;
        }

        if (key.Pressed && !key.Echo &&
            GameplayKeyMap.Matches(
                key, InputPreferences, KeybindAction.ToolAction))
        {
            ToolActionRequested?.Invoke();
        }
    }

    /// <summary>
    /// Implements MineClone's configured F5 camera cycle with Godot's
    /// collision-aware SpringArm. The player movement/body remains authoritative.
    /// </summary>
    public void CycleCameraPerspective()
    {
        if (_inputSuspended) return;
        _cameraView = _cameraView switch
        {
            CameraView.FirstPerson => CameraView.ThirdPersonBack,
            CameraView.ThirdPersonBack => CameraView.ThirdPersonFront,
            _ => CameraView.FirstPerson,
        };
        ApplyCameraView();
    }

    private void ApplyCameraView()
    {
        _cameraPivot.Rotation = new Vector3(
            _cameraPitch,
            _cameraView == CameraView.ThirdPersonFront ? Mathf.Pi : 0f,
            0f);
        _cameraArm.SpringLength = _cameraView == CameraView.FirstPerson
            ? 0f : ThirdPersonCameraDistance;
    }

    public void ResumeGameplay()
    {
        if (!_inputSuspended && !_mouseCaptured)
        {
            CaptureMouse();
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
