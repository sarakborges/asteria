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
    private Material? _tintMaterial;
    private (int X, int Z)? _tintColumn;

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
        PackContentRegistry<ObjectDefinition> definitions,
        BiomeTintField tints)
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
            var mesh = _visuals.GetMesh(definition);
            _instance.Mesh = mesh;
            _shownId = definition.Id;
            _tintColumn = null;
            var sourceMaterial = mesh.SurfaceGetMaterial(0) ??
                throw new InvalidOperationException("Object mesh has no material.");
            _tintMaterial = sourceMaterial is ShaderMaterial ||
                definition.Tint != BlockTint.None
                    ? (Material)sourceMaterial.Duplicate()
                    : null;
            if (_tintMaterial is ShaderMaterial shader)
                shader.SetShaderParameter("preview_mode", true);
            _instance.MaterialOverride = _tintMaterial;
        }

        if (_tintMaterial is not null)
        {
            var location = (placement.Support.X, placement.Support.Z);
            if (_tintColumn != location)
            {
                var coord = VoxelCoordinates.FromWorld(
                    placement.Support.X, placement.Support.Y,
                    placement.Support.Z).Chunk;
                var sample = tints.SampleChunkGrid(coord.X, coord.Z);
                var tint = sample.Resolve(definition.Tint, definition.TintFallback,
                    location.Item1, location.Item2);
                var color = new Color(tint.X, tint.Y, tint.Z);
                if (_tintMaterial is StandardMaterial3D standard)
                    standard.AlbedoColor = color;
                else if (_tintMaterial is ShaderMaterial shader)
                    shader.SetShaderParameter("preview_tint",
                        new Vector3(tint.X, tint.Y, tint.Z));
                _tintColumn = location;
            }
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
