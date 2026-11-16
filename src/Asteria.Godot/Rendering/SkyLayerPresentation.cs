using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Bounded sky presentation: two MultiMeshes, zero per-star/cloud nodes.
/// The authored geometry comes from Core; only camera and wind animation
/// are presentation-local and are retired with the Sphere.
/// </summary>
internal sealed class SkyLayerPresentation
{
    private const float CloudSpeed = 1.6f;
    private readonly Node _parent;
    private readonly SkyLayerPattern.CloudPart[] _cloudParts =
        Enumerable.Range(0, SkyLayerPattern.CloudCount)
            .SelectMany(index =>
                Enumerable.Range(0, SkyLayerPattern.CloudPartsPerCloud)
                    .Select(part => SkyLayerPattern.CloudPartAt(index, part)))
            .ToArray();
    private MultiMeshInstance3D? _stars;
    private MultiMeshInstance3D? _clouds;
    private StandardMaterial3D? _starMaterial;
    private DimensionSkyLayersDefinition _visuals = DimensionSkyLayersDefinition.Disabled;
    private DimensionWindDefinition _wind;
    private int _seaLevel;
    private double _cloudSeconds;

    public SkyLayerPresentation(Node parent) =>
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));

    public void Apply(DimensionDefinition dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        Clear();
        _visuals = dimension.Environment.SkyLayers;
        _wind = dimension.Environment.Wind;
        _seaLevel = dimension.SeaLevel;

        if (_visuals.StarDensity > 0f)
        {
            _starMaterial = NewMaterial(_visuals.StarColor, 0f, billboard: true);
            var instances = CreateBatch(new QuadMesh(), SkyLayerPattern.StarCount);
            instances.VisibleInstanceCount = (int)MathF.Round(
                SkyLayerPattern.StarCount * _visuals.StarDensity);
            for (var i = 0; i < SkyLayerPattern.StarCount; i++)
            {
                var star = SkyLayerPattern.StarAt(i);
                instances.SetInstanceTransform(i, new Transform3D(
                    Basis.Identity.Scaled(new Vector3(star.Size, star.Size, 1f)),
                    new Vector3(star.X, star.Y, star.Z) *
                        SkyLayerPattern.StarDistance));
            }
            _stars = Attach("SkyStars", instances, _starMaterial);
            _stars.Visible = false;
        }

        if (_visuals.CloudDensity > 0f)
        {
            var material = NewMaterial(_visuals.CloudColor, 0.78f, billboard: false);
            var instances = CreateBatch(new BoxMesh(), _cloudParts.Length);
            instances.VisibleInstanceCount =
                (int)MathF.Round(SkyLayerPattern.CloudCount * _visuals.CloudDensity) *
                SkyLayerPattern.CloudPartsPerCloud;
            _clouds = Attach("SkyClouds", instances, material);
            _clouds.Visible = false; // only publish after the first camera sample
        }
    }

    public void Update(DayNightClock clock)
    {
        if (_stars is null || _starMaterial is null)
            return;

        var sample = clock.Sample;
        var alpha = SkyLayerPattern.StarVisibility(sample);
        _stars.Visible = alpha > 0f;
        _starMaterial.AlbedoColor = ColorWithAlpha(_visuals.StarColor, alpha);
        _stars.Rotation = new Vector3(
            0f, -(float)sample.NormalizedTime * Mathf.Tau, 0f);
    }

    public void FollowCamera(Vector3 position, double deltaSeconds)
    {
        if (_stars is not null)
            _stars.GlobalPosition = position;

        if (_clouds is null)
            return;

        _cloudSeconds = (_cloudSeconds + Math.Max(0d, deltaSeconds)) % 3600d;
        var velocity = _wind.Velocity * CloudSpeed;
        var driftX = (float)(_cloudSeconds * velocity.X % SkyLayerPattern.CloudSpan);
        var driftZ = (float)(_cloudSeconds * velocity.Y % SkyLayerPattern.CloudSpan);
        var batch = _clouds.Multimesh;
        for (var i = 0; i < batch.VisibleInstanceCount; i++)
        {
            var part = _cloudParts[i];
            var x = SkyLayerPattern.WorldTileCoordinate(
                part.BaseX, driftX, position.X);
            var z = SkyLayerPattern.WorldTileCoordinate(
                part.BaseZ, driftZ, position.Z);
            var origin = new Vector3(
                x + part.OffsetX,
                _seaLevel + part.AltitudeAboveSeaLevel + part.OffsetY,
                z + part.OffsetZ);
            batch.SetInstanceTransform(i, new Transform3D(
                Basis.Identity.Scaled(new Vector3(
                    part.Width, part.Height, part.Depth)), origin));
        }
        _clouds.Visible = true;
    }

    public void Clear()
    {
        _stars?.QueueFree();
        _clouds?.QueueFree();
        _stars = null;
        _clouds = null;
        _starMaterial = null;
        _cloudSeconds = 0d;
        _visuals = DimensionSkyLayersDefinition.Disabled;
    }

    private static MultiMesh CreateBatch(Mesh mesh, int count) =>
        new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = count,
        };

    private MultiMeshInstance3D Attach(
        string name, MultiMesh instances, StandardMaterial3D material)
    {
        var node = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = instances,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _parent.AddChild(node);
        return node;
    }

    private static StandardMaterial3D NewMaterial(
        DimensionColor color, float alpha, bool billboard) =>
        new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = billboard
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableFog = true,
            AlbedoColor = ColorWithAlpha(color, alpha),
        };

    private static Color ColorWithAlpha(DimensionColor color, float alpha) =>
        new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha);
}
