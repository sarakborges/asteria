using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One reusable non-colliding Object placement ghost. Both presentation and
/// world batches use the same authored mesh contract and Core world pose.
/// </summary>
public sealed class ObjectPlacementPreviewPresentation
{
    private readonly WorldObjectVisualCatalog _visuals;
    private readonly MeshInstance3D _instance;
    private string? _shownId;

    public ObjectPlacementPreviewPresentation(Node3D parent, PackSelection pack)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _visuals = new WorldObjectVisualCatalog(pack);
        _instance = new MeshInstance3D
        {
            Name = "ObjectPlacementPreview",
            Visible = false,
            Transparency = 0.38f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(_instance);
    }

    public void Sync(ObjectPlacementDecision placement,
        PackContentRegistry<ObjectDefinition> definitions)
    {
        if (!placement.Accepted ||
            !definitions.TryGet(placement.Object.Id, out var definition) ||
            definition is null)
        {
            Hide();
            return;
        }

        if (_shownId != definition.Id)
        {
            _instance.Mesh = _visuals.GetMesh(definition);
            _shownId = definition.Id;
        }

        var pose = WorldObjectGeometry.Pose(
            placement.Support, placement.Object, definition);
        _instance.Position = new Vector3(pose.Position.X,
            pose.Position.Y, pose.Position.Z);
        _instance.Quaternion = new Quaternion(pose.Rotation.X,
            pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
        _instance.Scale = new Vector3(pose.Scale.X,
            pose.Scale.Y, pose.Scale.Z);
        _instance.Visible = true;
    }

    public void Hide() => _instance.Visible = false;
}
