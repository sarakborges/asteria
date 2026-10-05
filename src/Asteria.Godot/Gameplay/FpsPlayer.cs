using Godot;

namespace Asteria.Client.Gameplay;

public partial class FpsPlayer : CharacterBody3D
{
    private const float MoveSpeed = 7.5f;
    private const float JumpSpeed = 8.0f;
    private const float Gravity = 24.0f;
    private const float MouseSensitivity = 0.0022f;
    private const float MaxPitch = 1.52f;

    private Camera3D _camera = null!;
    private bool _mouseCaptured = true;

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

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key &&
            key.Pressed &&
            !key.Echo &&
            (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape))
        {
            if (_mouseCaptured)
            {
                ReleaseMouse();
            }
            else
            {
                CaptureMouse();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (_mouseCaptured && inputEvent is InputEventMouseMotion mouseMotion)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);
            _camera.Rotation = new Vector3(
                Mathf.Clamp(_camera.Rotation.X - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitch, MaxPitch),
                0f,
                0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;

        if (IsOnFloor())
        {
            if (Input.IsPhysicalKeyPressed(Key.Space))
            {
                velocity.Y = JumpSpeed;
            }
        }
        else
        {
            velocity.Y -= Gravity * (float)delta;
        }

        var movement = Vector2.Zero;

        if (Input.IsPhysicalKeyPressed(Key.W))
            movement.Y -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S))
            movement.Y += 1f;
        if (Input.IsPhysicalKeyPressed(Key.A))
            movement.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D))
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

    public override void _ExitTree()
    {
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private void CaptureMouse()
    {
        _mouseCaptured = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ReleaseMouse()
    {
        _mouseCaptured = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
