using Asteria.Core.World;
using Asteria.Core.Settings;
using Godot;

namespace Asteria.Client.Gameplay;

public partial class FpsPlayer : CharacterBody3D
{
    private const float MoveSpeed = PlayerMovementSpeeds.Walk;
    private const float JumpSpeed = PlayerMovementSpeeds.Jump;
    private const float MouseSensitivity = 0.0022f;
    private const float MaxPitch = 1.52f;
    private const float ThirdPersonCameraDistance = 4f;
    private const float StandingHeight = PlayerMovementGeometry.StandingHeight;
    private const float CrouchHeight = PlayerMovementGeometry.CrouchHeight;
    private const float StandingEyeHeight = PlayerMovementGeometry.EyeHeight;
    private const float CrouchEyeDrop = 0.35f;
    private const float CrouchCameraBlendSpeed = 8f;
    private const float WalkAcceleration = PlayerMovementSpeeds.WalkAcceleration;
    private const float WalkDeceleration = PlayerMovementSpeeds.WalkDeceleration;
    private const float CrouchEdgeProbe = 0.12f;
    private const float MaximumImpactHorizontalSpeed = 18f;

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
    private BoxShape3D _standingBox = null!;
    private BoxShape3D _crouchedBox = null!;
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
    private float _descendingDistance;
    // Presentation-only smooth camera offset after a collision-checked step.
    private float _stepVisualOffsetY;

    public event Action? BreakRequested;
    public event Action? PlaceRequested;
    public event Action? PickBlockRequested;
    public event Action? ToolActionRequested;
    public event Action? DropItemRequested;
    public event Action<int>? HotbarSlotRequested;
    public event Action<float>? Landed;
    public event Action? FlightStateChanged;
    public event Action<bool>? MouseCaptureChanged;
    public event Action<FluidBodyContact>?
        FluidContactChanged;
    public event Action<float, FluidBodyContact>? FluidExposureSampled;

    public PlayerSessionState PlayerState { get; set; } = null!;

    public ClientPreferences InputPreferences { get; set; } = null!;

    public Func<ulong> CurrentWorldTick { get; set; } = null!;

    public bool IsMouseCaptured => _mouseCaptured;
    public bool IsBreakHeld => _breakHeld && _mouseCaptured && !_inputSuspended;
    public bool IsRunning => _groundMovement.IsRunning;
    public bool IsCrouching => _groundMovement.IsCrouching;
    public bool IsThirdPerson => _cameraView != CameraView.FirstPerson;
    // The front camera observes the world in the opposite direction while
    // movement and the player's body yaw remain unchanged.
    public bool IsThirdPersonFront => _cameraView == CameraView.ThirdPersonFront;
    public float CameraPitch => _cameraPitch;

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

    /// <summary>Applies one accepted creature attack as physical velocity.
    /// Godot MoveAndSlide and the normal movement acceleration own collision,
    /// friction and return to normal movement. No teleport or extra timer.</summary>
    public void ApplyCreatureImpact(System.Numerics.Vector3 impulse)
    {
        if (!float.IsFinite(impulse.X) ||
            !float.IsFinite(impulse.Y) ||
            !float.IsFinite(impulse.Z))
            throw new ArgumentOutOfRangeException(nameof(impulse));
        if (PlayerState.Health.IsDead || PlayerState.IsFlying ||
            PlayerState.GameMode != PlayerGameMode.Survival)
            return;

        var velocity = Velocity;
        var horizontal = new Vector2(velocity.X + impulse.X,
            velocity.Z + impulse.Z).LimitLength(MaximumImpactHorizontalSpeed);
        Velocity = new Vector3(horizontal.X,
            Mathf.Max(velocity.Y, impulse.Y), horizontal.Y);
    }

    public override void _Ready()
    {
        ArgumentNullException.ThrowIfNull(PlayerState);
        ArgumentNullException.ThrowIfNull(InputPreferences);
        ArgumentNullException.ThrowIfNull(CurrentWorldTick);

        _solidCollisionLayer = CollisionLayer;
        _solidCollisionMask = CollisionMask;
        // MineClone's voxel collision bounds are an AABB, not a capsule.
        // The same half-width is used for collision, placement and fluids.
        _standingBox = new BoxShape3D
        {
            Size = new Vector3(
                PlayerMovementGeometry.Width, StandingHeight,
                PlayerMovementGeometry.Width),
        };
        _crouchedBox = new BoxShape3D
        {
            Size = new Vector3(
                PlayerMovementGeometry.Width, CrouchHeight,
                PlayerMovementGeometry.Width),
        };
        _collider = new CollisionShape3D
        {
            Name = "Collider",
            Position = new Vector3(0f, StandingHeight * 0.5f, 0f),
            Shape = _standingBox,
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
        // Player activation can finish after Alt+Tab during world loading.
        // Apply the same OS-focus gate as explicit resume.
        ResumeGameplay();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_inputSuspended || PlayerState.Health.IsDead)
            return;

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
                case MouseButton.Middle:
                    PickBlockRequested?.Invoke();
                    break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (PlayerState.Health.IsDead)
        {
            _groundMovement.ConsumeGroundJump();
            _descendingDistance = 0f;
            Velocity = Vector3.Zero;
            return;
        }
        // Consume once per physics tick, including air/flight/fluid frames:
        // a press made in mid-air cannot queue a jump on a later landing.
        var groundJumpPressed = _groundMovement.ConsumeGroundJump();
        var startedOnFloor = IsOnFloor();
        var frameOrigin = GlobalPosition;
        var startY = frameOrigin.Y;
        var velocity = Velocity;
        var movement = GetMovementInput();
        var direction = GlobalTransform.Basis *
            new Vector3(movement.X, 0f, movement.Y);
        direction.Y = 0f;
        direction = direction.Normalized();

        var fluidContact = default(FluidBodyContact);
        if (PlayerState.IsFlying)
        {
            _descendingDistance = 0f;
            UpdateGroundPosture(immersed: false, (float)delta);
            _groundMovement.CancelRunning();
        }
        else
        {
            // Sample Core-owned fluid contact once per physics step,
            // before changing posture, and reuse it for swim response.
            fluidContact = FluidContactProvider?.Invoke(
                CollisionBounds, _cameraPivot.GlobalPosition.Y) ?? default;
            UpdateGroundPosture(fluidContact.IsImmersed, (float)delta);
        }

        if (PlayerState.IsFlying)
        {
            var flySpeed = PlayerMovementSpeeds.Flight;
            var vertical = (_jumpHeld ? 1f : 0f) -
                (_descendHeld ? 1f : 0f);
            var target = direction * flySpeed +
                Vector3.Up * (vertical * flySpeed);
            var current = Velocity;
            var next = PlayerMovementPhysics.FlightVelocity(
                new System.Numerics.Vector3(current.X, current.Y, current.Z),
                new System.Numerics.Vector3(target.X, target.Y, target.Z),
                (float)delta);
            Velocity = new Vector3(next.X, next.Y, next.Z);

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

        PublishFluidContact(fluidContact);
        FluidExposureSampled?.Invoke((float)Math.Min(delta, 0.25), fluidContact);

        var fluidMotion =
            fluidContact.IsImmersed
                ? FluidMotionProvider?.Invoke(
                      fluidContact.Fluid) ??
                  FluidMotionDefinition.Default
                : default;

        if (fluidContact.IsImmersed)
        {
            _descendingDistance = 0f;
            velocity.Y =
                FluidMotionSolver.VerticalSpeed(
                    velocity.Y,
                    (float)delta,
                    fluidContact,
                    fluidMotion,
                    _jumpHeld,
                    _descendHeld);
        }
        else if (IsOnFloor())
        {
            if (groundJumpPressed)
            {
                velocity.Y = JumpSpeed;
            }
        }
        else
        {
            velocity.Y = PlayerMovementPhysics.ApplyGravity(
                velocity.Y, GravityStrength, (float)delta);
        }

        if (fluidContact.IsImmersed)
        {
            var targetSpeed =
                MoveSpeed *
                fluidMotion.HorizontalSpeedMultiplier;
            var target = new Vector2(direction.X, direction.Z) *
                targetSpeed;
            var horizontal = new Vector2(velocity.X, velocity.Z);
            var acceleration = (target == Vector2.Zero
                ? WalkDeceleration : fluidMotion.HorizontalAcceleration) *
                (float)delta;
            horizontal = horizontal.MoveToward(target, acceleration);
            velocity.X = horizontal.X;
            velocity.Z = horizontal.Y;
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

        // Only the actual published native terrain collision can authorize
        // a step. No voxel guess, blind teleport or mid-air stair-climbing.
        if (startedOnFloor && !fluidContact.IsImmersed &&
            !groundJumpPressed && velocity.Y <= 0f)
        {
            TryStepUp(frameOrigin, velocity, (float)delta);
        }

        if (!fluidContact.IsImmersed)
        {
            _descendingDistance += Mathf.Max(0f, startY - GlobalPosition.Y);
            if (IsOnFloor())
            {
                if (!startedOnFloor && _descendingDistance > 0f)
                    Landed?.Invoke(_descendingDistance);
                _descendingDistance = 0f;
            }
        }
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
                ? _crouchedBox : _standingBox;
            _collider.Position = new Vector3(0f, height * 0.5f, 0f);
        }

        var targetEyeY = StandingEyeHeight -
            (_groundMovement.IsCrouching ? CrouchEyeDrop : 0f);
        var previousStepOffset = _stepVisualOffsetY;
        _stepVisualOffsetY = Mathf.MoveToward(
            _stepVisualOffsetY, 0f,
            PlayerMovementGeometry.StepSmoothingSpeed * delta);
        // Keep crouch interpolation independent of terrain-step smoothing.
        var crouchEyeY = _cameraPivot.Position.Y - previousStepOffset;
        _cameraPivot.Position = new Vector3(0f,
            Mathf.MoveToward(crouchEyeY, targetEyeY,
                CrouchEyeDrop * CrouchCameraBlendSpeed * delta) +
                _stepVisualOffsetY, 0f);
    }

    private bool CanStandAtCurrentPosition()
    {
        if (PlayerState.GameMode.IsSpectator())
            return true;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _standingBox,
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

    /// <summary>
    /// Bounded Godot motion sweeps for MineClone's eighth-block stepping.
    /// Each candidate must clear the rise and horizontal path, then settle on
    /// a walkable static terrain surface. The actual body stays collision-safe;
    /// only the relative camera offset is smoothed over subsequent frames.
    /// </summary>
    private bool TryStepUp(Vector3 frameOrigin,
        Vector3 requestedVelocity, float deltaSeconds)
    {
        var requestedHorizontal = new Vector3(
            requestedVelocity.X, 0f, requestedVelocity.Z) * deltaSeconds;
        var actual = GlobalPosition - frameOrigin;
        var remaining = requestedHorizontal -
            new Vector3(actual.X, 0f, actual.Z);
        if (remaining.LengthSquared() < 0.000001f)
            return false;

        var hitStaticWall = false;
        for (var index = 0; index < GetSlideCollisionCount(); index++)
        {
            var slide = GetSlideCollision(index);
            var normal = slide.GetNormal();
            if (slide.GetCollider() is StaticBody3D &&
                Mathf.Abs(normal.Y) < 0.5f &&
                normal.Dot(remaining) < -0.0001f)
            {
                hitStaticWall = true;
                break;
            }
        }
        if (!hitStaticWall)
            return false;

        var from = GlobalTransform;
        for (var index = 0;
             index < PlayerMovementGeometry.StepCandidateCount;
             index++)
        {
            var rise = PlayerMovementGeometry.StepRiseAt(index);
            var upward = Vector3.Up * rise;
            if (TestMove(from, upward))
                continue;
            var raised = from.Translated(upward);
            if (TestMove(raised, remaining))
                continue;

            var landingStart = raised.Translated(remaining);
            var landing = new KinematicCollision3D();
            if (!TestMove(landingStart,
                    Vector3.Down * (rise + PlayerMovementGeometry.GroundProbe),
                    landing) ||
                landing.GetNormal().Y < 0.7f)
                continue;

            var landedRise = rise + landing.GetTravel().Y;
            if (landedRise < PlayerMovementGeometry.StepMinimumRise ||
                landedRise > PlayerMovementGeometry.MaximumStepHeight +
                    PlayerMovementGeometry.GroundProbe)
                continue;

            GlobalPosition = landingStart.Origin + landing.GetTravel();
            Velocity = new Vector3(
                requestedVelocity.X, 0f, requestedVelocity.Z);
            _stepVisualOffsetY -= landedRise;
            _cameraPivot.Position += Vector3.Down * landedRise;
            ApplyFloorSnap();
            return true;
        }
        return false;
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
        _descendingDistance = 0f;
        _stepVisualOffsetY = 0f;
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
        // Never force a standing capsule through a low ceiling when
        // pausing, releasing mouse capture or opening inventory.
        // The next physics tick checks clearance before standing.
        _groundMovement.ReleaseInput();
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
            const float halfWidth = PlayerMovementGeometry.HalfWidth;
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
        // The composition root routes Escape, including mouse release
        // and UI navigation, so a single keypress cannot do both.
        if (key.Keycode == Key.Escape)
            return;

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
                _groundMovement.QueueGroundJump();
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
        // A WebUI close/submit acknowledgement may arrive after Alt+Tab.
        // Never recapture the OS pointer unless the Godot window owns focus.
        // Paused gameplay remains uncaptured until the player explicitly
        // resumes from a focused window.
        if (!_inputSuspended && !_mouseCaptured &&
            IsInsideTree() && GetWindow().HasFocus())
        {
            CaptureMouse();
        }
    }

    // Losing OS focus must release held movement/buttons as well as the cursor.
    // MouseCaptureChanged drives the existing pause-menu navigation.
    public void PauseGameplay()
    {
        if (_mouseCaptured)
            ReleaseMouse();
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
