using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Lazily renders each targeted creature model into a transparent, cached
/// portrait. The GLB and its authored textures are handled by the same
/// presentation factory as creatures in the world.
/// </summary>
public sealed partial class CreaturePortraitPresentation : Node
{
    private const int Size = 128;
    private const int MaximumPngBytes = 128 * 1024;
    private readonly PackSelection _pack;
    private readonly PackContentRegistry<CreatureDefinition> _definitions;
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _requested = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _portraits =
        new(StringComparer.Ordinal);

    private SubViewport _viewport = null!;
    private Node3D _modelRoot = null!;
    private Camera3D _camera = null!;
    private CreaturePresentationController _modelFactory = null!;
    private Node3D? _activeModel;
    private string? _activeId;
    private int _framesUntilCapture;

    public CreaturePortraitPresentation(
        PackSelection pack,
        PackContentRegistry<CreatureDefinition> definitions)
    {
        _pack = pack;
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));
        Name = "CreaturePortrait";
    }

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Name = "CreaturePortraitViewport",
            Size = new Vector2I(Size, Size),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);
        _modelRoot = new Node3D { Name = "PortraitModelRoot" };
        _viewport.AddChild(_modelRoot);
        _camera = new Camera3D
        {
            Current = true,
            Fov = 38f,
            Near = 0.1f,
            Far = 50f,
        };
        _viewport.AddChild(_camera);
        _modelFactory = new CreaturePresentationController(
            _modelRoot, _pack, _definitions);
    }

    public string? GetOrRequest(string creatureId)
    {
        if (_portraits.TryGetValue(creatureId, out var image))
            return image;
        if (_requested.Add(creatureId))
            _queue.Enqueue(creatureId);
        return null;
    }

    public override void _Process(double delta)
    {
        if (_framesUntilCapture > 0)
        {
            if (--_framesUntilCapture == 0)
                Capture();
            return;
        }

        if (_activeId is not null || !_queue.TryDequeue(out var id))
            return;

        try
        {
            var definition = _definitions.Get(id);
            _activeModel = _modelFactory.CreatePreviewModel(definition);
            _activeModel.Rotation = new Vector3(0f, Mathf.Pi, 0f);
            _modelRoot.AddChild(_activeModel);

            var bounds = definition.Collider.Size;
            var height = Mathf.Max(bounds.Y, 0.4f);
            var radius = Mathf.Max(bounds.X, Mathf.Max(bounds.Z, height));
            var center = new Vector3(0f, height * 0.5f, 0f);
            _camera.Position = center + new Vector3(0f, 0f, radius * 2.3f + 0.65f);
            _camera.LookAt(center, Vector3.Up);

            _activeId = id;
            _framesUntilCapture = 3;
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
        catch (Exception error)
        {
            GD.PushWarning($"Creature portrait {id}: {error.Message}");
            ClearActive();
        }
    }

    private void Capture()
    {
        try
        {
            var image = _viewport.GetTexture().GetImage();
            if (image is not null && !image.IsEmpty())
            {
                using (image)
                {
                    var png = image.SavePngToBuffer();
                    if (png.Length is > 8 and <= MaximumPngBytes &&
                        _activeId is { } id)
                        _portraits[id] = "data:image/png;base64," +
                            Convert.ToBase64String(png);
                }
            }
        }
        finally
        {
            ClearActive();
        }
    }

    private void ClearActive()
    {
        if (_activeModel is { } model)
        {
            _modelRoot.RemoveChild(model);
            model.Free();
        }
        _activeModel = null;
        _activeId = null;
        _framesUntilCapture = 0;
    }

    public override void _ExitTree()
    {
        _modelFactory?.Retire();
    }
}
