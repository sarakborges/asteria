using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Camera-local first-person arm and selected item. This is a visual consumer
/// of the existing player, inventory, and selected-pack model; it never owns
/// gameplay input or action timing.
/// </summary>
internal sealed partial class PlayerFirstPersonViewModel : Node3D
{
    // MineClone's arm framing and three one-shot action poses, adapted to
    // Godot camera-local coordinates (forward is -Z).
    private static readonly Vector3 RestPosition = new(0.62f, -0.80f, -1.05f);
    private static readonly Vector3 RestRotation = new(-0.22f, -0.10f, 0.28f);
    internal static readonly Quaternion RestPoseRotation = EulerXyz(RestRotation);
    private const ulong SwitchDurationTicks = 12;
    private const ulong BreakDurationTicks = 22;
    private const ulong FastBreakDurationTicks = 8;
    private const ulong HitDurationTicks = 17;
    private const ulong PlaceDurationTicks = 19;

    private readonly FpsPlayer _player;
    private readonly PackSelection _pack;
    private readonly PlayerVisualDefinition _definition;
    private readonly PlayerInventory _inventory;
    private readonly HeldVisualResolver _heldResolver;
    private Node3D _pose = null!;
    private HeldItemPresentation _held = null!;
    private ulong _observedInventoryRevision = ulong.MaxValue;
    private HeldVisual? _selectedVisual;
    private bool _switching;
    private ulong _switchStartTick;
    private PlayerVisualAction? _action;
    private ulong _actionStartTick;
    private bool _fastBreak;

    public PlayerFirstPersonViewModel(
        FpsPlayer player, PackSelection pack,
        PlayerVisualDefinition definition, PlayerInventory inventory,
        HeldVisualResolver heldResolver)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _pack = pack;
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _heldResolver = heldResolver ?? throw new ArgumentNullException(nameof(heldResolver));
        Name = "FirstPersonViewModel";
        Visible = false;
    }

    public override void _Ready()
    {
        _pose = new Node3D { Name = "ViewModelPose" };
        AddChild(_pose);

        var armReference = new Node3D
        {
            Name = "ArmReference",
            Rotation = new Vector3(0f, 0f, Mathf.Pi),
        };
        _pose.AddChild(armReference);

        // Reuse the actual textured and UV-mapped GLB arm, including its
        // outer-skin sleeve. Only the right arm is visible in this camera.
        var model = PlayerVisualSceneFactory.Load(_pack, _definition);
        armReference.AddChild(model);
        var pivot = PlayerVisualSceneFactory.Descendants(model)
            .OfType<Node3D>()
            .FirstOrDefault(node => node.Name.ToString() == "RightArmPivot")
            ?? throw new InvalidDataException(
                "Authored player GLB is missing its first-person right arm pivot.");
        var armMeshes = PlayerVisualSceneFactory.Descendants(pivot)
            .OfType<MeshInstance3D>()
            .ToHashSet();
        if (armMeshes.Count == 0)
            throw new InvalidDataException(
                "Authored player GLB has no right-arm mesh.");

        foreach (var mesh in PlayerVisualSceneFactory.Descendants(model)
                     .OfType<MeshInstance3D>())
            if (!armMeshes.Contains(mesh))
                mesh.Visible = false;

        // Normalizes the original shoulder pivot around the animated pose,
        // rather than copying a second handmade arm/UV layout.
        var armRotation = new Basis(Quaternion.FromEuler(armReference.Rotation));
        armReference.Position = -(armRotation * pivot.Position);
        // MineClone keeps the held item alongside the arm under the camera-
        // local pose, not under the arm's shoulder pivot.
        _held = new HeldItemPresentation(_pack, firstPerson: true);
        _pose.AddChild(_held);
        _selectedVisual = _heldResolver.Resolve(_inventory.SelectedStack);
        _held.SetVisual(_selectedVisual);
        _observedInventoryRevision = _inventory.Revision;
        UpdatePose(_player.CurrentWorldTick());
    }

    public bool TryPlayAction(PlayerVisualAction action, bool fastBreak = false)
    {
        if (action is not (PlayerVisualAction.Hit or PlayerVisualAction.Break or
            PlayerVisualAction.Place))
            return false;
        _action = action;
        _fastBreak = action == PlayerVisualAction.Break && fastBreak;
        _actionStartTick = _player.CurrentWorldTick();
        return true;
    }

    public override void _Process(double delta)
    {
        Visible = !_player.IsThirdPerson &&
            !_player.PlayerState.GameMode.IsSpectator() &&
            !_player.PlayerState.Health.IsDead &&
            _player.IsMouseCaptured;

        var tick = _player.CurrentWorldTick();
        if (_observedInventoryRevision != _inventory.Revision)
        {
            var selected = _heldResolver.Resolve(_inventory.SelectedStack);
            if (!Equals(selected, _selectedVisual))
            {
                _switching = true;
                _switchStartTick = tick;
            }
            _held.SetVisual(selected);
            _selectedVisual = selected;
            _observedInventoryRevision = _inventory.Revision;
        }

        if (Visible || _action is not null || _switching)
            UpdatePose(tick);
    }

    private void UpdatePose(ulong tick)
    {
        var position = RestPosition;
        var rotation = RestPoseRotation;
        if (_action is { } action)
        {
            var duration = action switch
            {
                PlayerVisualAction.Break => _fastBreak
                    ? FastBreakDurationTicks : BreakDurationTicks,
                PlayerVisualAction.Hit => HitDurationTicks,
                _ => PlaceDurationTicks,
            };
            var wave = Wave(tick, _actionStartTick, duration);
            if (wave <= 0f && tick >= _actionStartTick + duration)
                _action = null;
            var displacement = action switch
            {
                PlayerVisualAction.Break => new Vector3(-0.03f, 0.01f, -0.14f),
                PlayerVisualAction.Hit => new Vector3(-0.05f, -0.02f, -0.18f),
                _ => new Vector3(-0.06f, -0.10f, -0.06f),
            };
            var turn = action switch
            {
                PlayerVisualAction.Break => new Vector3(-0.18f, 0.05f, -0.08f),
                PlayerVisualAction.Hit => new Vector3(-0.30f, 0.10f, -0.14f),
                _ => new Vector3(-0.68f, 0.12f, -0.34f),
            };
            position += displacement * wave;
            rotation *= EulerXyz(turn * wave);
        }

        if (_switching)
        {
            var wave = Wave(tick, _switchStartTick, SwitchDurationTicks);
            if (tick >= _switchStartTick + SwitchDurationTicks)
                _switching = false;
            position += new Vector3(0.10f, -0.54f, 0.14f) * wave;
            rotation *= EulerXyz(
                new Vector3(0.34f, 0f, 0.16f) * wave);
        }

        _pose.Position = position;
        _pose.Quaternion = rotation;
    }

    // Bevy's authored viewmodel poses use XYZ Euler order; Godot's
    // Quaternion.FromEuler defaults to YXZ and visibly changes the hand angle.
    private static Quaternion EulerXyz(Vector3 angles) =>
        new Quaternion(Vector3.Right, angles.X) *
        new Quaternion(Vector3.Up, angles.Y) *
        new Quaternion(Vector3.Back, angles.Z);

    private static float Wave(ulong tick, ulong start, ulong duration)
    {
        if (tick < start || tick - start >= duration)
            return 0f;
        return Mathf.Sin(Mathf.Pi * (tick - start) / duration);
    }
}
