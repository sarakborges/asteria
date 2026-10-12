using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Isolated, visual-only 3D portrait. Produces bounded PNG snapshots only
/// while the character panel requests them, never every rendered frame.
/// Input is semantic drag delta from an explicitly focused UI surface.
/// </summary>
public sealed partial class PlayerPortraitPresentation : Node
{
    private const int Size = 256;
    private const int MaxPngBytes = 320 * 1024;
    private readonly PackSelection _selection;
    private readonly PlayerVisualDefinition _definition;
    private readonly PlayerInventory _inventory;
    private readonly PackContentRegistry<ItemDefinition> _items;
    private readonly HeldVisualResolver _heldResolver;
    private readonly WorldObjectVisualCatalog _objectVisuals;
    private readonly Func<Color?> _selectedToolTint;
    private PlayerEquipmentPresentation? _equipment;
    private HeldItemPresentation? _held;
    private SubViewport _viewport = null!;
    private Node3D _facing = null!;
    private int _framesUntilCapture;
    private string? _available;
    private float _yaw;

    public PlayerPortraitPresentation(
        PackSelection selection, PlayerVisualDefinition definition,
        PlayerInventory inventory, PackContentRegistry<ItemDefinition> items,
        HeldVisualResolver heldResolver, Func<Color?> selectedToolTint,
        WorldObjectVisualCatalog objectVisuals)
    {
        _selection = selection;
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _heldResolver = heldResolver ?? throw new ArgumentNullException(nameof(heldResolver));
        _objectVisuals = objectVisuals ??
            throw new ArgumentNullException(nameof(objectVisuals));
        _selectedToolTint = selectedToolTint ??
            throw new ArgumentNullException(nameof(selectedToolTint));
        Name = "PlayerPortrait";
    }

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Name = "PortraitViewport",
            Size = new Vector2I(Size, Size),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);

        _facing = new Node3D
        {
            Name = "PortraitModel",
            Rotation = new Vector3(0f, Mathf.Pi, 0f)
        };
        _viewport.AddChild(_facing);
        var model = PlayerVisualSceneFactory.Load(_selection, _definition);
        _facing.AddChild(model);
        _equipment = new PlayerEquipmentPresentation(model, _inventory, _items);
        _equipment.Sync();
        var hand = PlayerVisualSceneFactory.Descendants(model)
            .OfType<Node3D>()
            .FirstOrDefault(node => node.Name.ToString() == "RightArmPivot")
            ?? throw new InvalidDataException(
                "Authored player GLB is missing the portrait's right arm pivot.");
        _held = new HeldItemPresentation(_selection, _objectVisuals);
        hand.AddChild(_held);
        _held.SetVisual(_heldResolver.Resolve(_inventory.SelectedStack));
        _held.SetTint(_selectedToolTint());
        var animation = PlayerVisualSceneFactory.Descendants(model)
            .OfType<AnimationPlayer>().FirstOrDefault();
        if (animation is not null)
        {
            var wanted = _definition.Animations["idle"];
            var idle = animation.GetAnimationList().Select(a => a.ToString())
                .FirstOrDefault(a => a == wanted ||
                    a.EndsWith("/" + wanted, StringComparison.Ordinal));
            if (idle is not null)
            {
                animation.GetAnimation(idle).LoopMode = Animation.LoopModeEnum.Linear;
                animation.Play(idle);
            }
        }

        var camera = new Camera3D
        {
            Name = "PortraitCamera",
            Current = true,
            // The model faces -Z after its 180-degree authored facing turn.
            Position = new Vector3(0f, 1.08f, -3.15f),
            Fov = 38f,
            Near = 0.1f,
            Far = 10f,
        };
        _viewport.AddChild(camera);
        camera.LookAt(new Vector3(0f, 0.93f, 0f), Vector3.Up);
    }

    public void RequestCapture()
    {
        if (_viewport is null) return;
        _equipment?.Sync();
        _held?.SetVisual(_heldResolver.Resolve(_inventory.SelectedStack));
        _held?.SetTint(_selectedToolTint());
        _available = null;
        _framesUntilCapture = 3;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    public void Rotate(float horizontalPixels)
    {
        if (!float.IsFinite(horizontalPixels)) return;
        _yaw = Mathf.Wrap(_yaw +
            Mathf.Clamp(horizontalPixels, -120f, 120f) * 0.009f,
            -Mathf.Pi, Mathf.Pi);
        _facing.Rotation = new Vector3(0f, Mathf.Pi + _yaw, 0f);
        RequestCapture();
    }

    public override void _Process(double delta)
    {
        if (_framesUntilCapture == 0) return;
        _framesUntilCapture--;
        if (_framesUntilCapture != 0) return;

        var frame = _viewport.GetTexture().GetImage();
        if (frame is null || frame.IsEmpty()) return;
        var png = frame.SavePngToBuffer();
        if (png.Length is < 8 or > MaxPngBytes) return;
        _available = "data:image/png;base64," + Convert.ToBase64String(png);
    }

    public bool TryTake(out string? data)
    {
        data = _available;
        _available = null;
        return data is not null;
    }
}
