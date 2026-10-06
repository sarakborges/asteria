using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

public partial class FpsPlayer : CharacterBody3D
{
    private const float MoveSpeed = 7.5f;
    private const float JumpSpeed = 8.0f;
    private const float Gravity = 24.0f;
    private const float SwimAscendSpeed = 3.8f;
    private const float SwimSinkSpeed = 0.9f;
    private const float SwimVerticalAcceleration = 12.0f;
    private const float SwimExitSurfaceMargin = 0.35f;
    private const float MouseSensitivity = 0.0022f;
    private const float MaxPitch = 1.52f;

    private Camera3D _camera = null!;
    private bool _mouseCaptured;
    private bool _moveForward;
    private bool _moveBackward;
    private bool _moveLeft;
    private bool _moveRight;
    private bool _jumpHeld;

    public event Action? BreakRequested;
    public event Action? PlaceRequested;
    public event Action<bool>? MouseCaptureChanged;

    public bool IsMouseCaptured => _mouseCaptured;

    public Func<WorldAabb, float, FluidBodyContact>?
        FluidContactProvider { get; set; }

    public override void _Ready()
    {
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
        CaptureMouse();
    }

    public override void _Input(InputEvent inputEvent)
    {
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

        var fluidContact =
            FluidContactProvider?.Invoke(
                CollisionBounds,
                _camera.GlobalPosition.Y) ??
            default;

        if (fluidContact.IsImmersed)
        {
            var targetVerticalSpeed =
                _jumpHeld
                    ? fluidContact.IsNearSurface(
                        SwimExitSurfaceMargin)
                        ? JumpSpeed
                        : SwimAscendSpeed
                    : -SwimSinkSpeed;

            velocity.Y =
                Mathf.MoveToward(
                    velocity.Y,
                    targetVerticalSpeed,
                    SwimVerticalAcceleration *
                    (float)delta);
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
                Gravity *
                (float)delta;
        }

        var movement = Vector2.Zero;

        if (_moveForward)
            movement.Y -= 1f;
        if (_moveBackward)
            movement.Y += 1f;
        if (_moveLeft)
            movement.X -= 1f;
        if (_moveRight)
            movement.X += 1f;

        movement = movement.LimitLength(1f);

        var direction = GlobalTransform.Basis * new Vector3(movement.X, 0f, movement.Y);
        direction.Y = 0f;
        direction = direction.Normalized();

        velocity.X = direction.X * MoveSpeed;
        velocity.Z = direction.Z * MoveSpeed;

        Velocity = velocity;
        MoveAndSlide();
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

    private void HandleKey(InputEventKey key)
    {
        var keycode = key.Keycode;

        switch (keycode)
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
            case Key.Space:
                _jumpHeld = key.Pressed;
                break;
            case Key.Escape when key.Pressed && !key.Echo:
                if (_mouseCaptured)
                {
                    ReleaseMouse();
                }
                else
                {
                    CaptureMouse();
                }
                break;
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
        Input.MouseMode = Input.MouseModeEnum.Visible;
        MouseCaptureChanged?.Invoke(false);
    }
}
