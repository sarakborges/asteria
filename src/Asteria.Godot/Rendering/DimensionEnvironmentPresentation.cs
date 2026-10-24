using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Presents sky layers and celestial sprites only. This presentation deliberately
/// creates no lights, shadow casters, or world-lighting environment.
/// </summary>
public sealed class DimensionEnvironmentPresentation
{
    private readonly Node _parent;
    private readonly SkyLayerPresentation _skyLayers;
    private MeshInstance3D? _sunOrb;
    private MeshInstance3D? _moonOrb;
    private DimensionDefinition? _dimension;
    private Vector3 _sunDirection;
    private Vector3 _moonDirection;
    private float _sunDistance;
    private float _moonDistance;
    private bool _cameraReady;
    private bool _sunActive;
    private bool _moonActive;

    public DimensionEnvironmentPresentation(Node parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _skyLayers = new SkyLayerPresentation(parent);
    }

    public void Apply(
        DimensionDefinition dimension,
        DayNightCycleDefinition cycle,
        DayNightClock clock,
        PackSelection selection)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(clock);

        _dimension = dimension;
        _cameraReady = false;
        _sunOrb ??= CreateOrb("DaySun");
        _moonOrb ??= CreateOrb("NightMoon");
        ConfigureOrb(_sunOrb, cycle.Sun, selection);
        ConfigureOrb(_moonOrb, cycle.Moon, selection);
        _skyLayers.Apply(dimension);
        Update(cycle, clock);
    }

    public void Clear()
    {
        _sunOrb?.QueueFree();
        _moonOrb?.QueueFree();
        _sunOrb = null;
        _moonOrb = null;
        _dimension = null;
        _cameraReady = false;
        _sunActive = false;
        _moonActive = false;
        _skyLayers.Clear();
    }

    public void Update(DayNightCycleDefinition cycle, DayNightClock clock)
    {
        if (_dimension is null)
            return;

        ApplyCelestial(_sunOrb!, cycle.Sun, cycle, clock, isSun: true);
        ApplyCelestial(_moonOrb!, cycle.Moon, cycle, clock, isSun: false);
        _skyLayers.Update(clock);
    }

    public void FollowCamera(Vector3 position, double deltaSeconds)
    {
        _cameraReady = true;
        _skyLayers.FollowCamera(position, deltaSeconds);
        if (_sunOrb is not null)
        {
            _sunOrb.Visible = _sunActive;
            if (_sunActive)
                _sunOrb.GlobalPosition = position + _sunDirection * _sunDistance;
        }
        if (_moonOrb is not null)
        {
            _moonOrb.Visible = _moonActive;
            if (_moonActive)
                _moonOrb.GlobalPosition = position + _moonDirection * _moonDistance;
        }
    }

    private MeshInstance3D CreateOrb(string name)
    {
        var mesh = new MeshInstance3D
        {
            Name = name,
            Mesh = new QuadMesh(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DisableFog = true,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _parent.AddChild(mesh);
        return mesh;
    }

    private static void ConfigureOrb(
        MeshInstance3D orb,
        DayNightCelestialDefinition? body,
        PackSelection selection)
    {
        if (orb.MaterialOverride is StandardMaterial3D material)
            material.AlbedoTexture = body?.Texture is { } texture
                ? ProjectPackFiles.LoadTexture(selection, texture)
                : null;
    }

    private void ApplyCelestial(
        MeshInstance3D orb,
        DayNightCelestialDefinition? body,
        DayNightCycleDefinition cycle,
        DayNightClock clock,
        bool isSun)
    {
        var offset = body is null
            ? null
            : CelestialPath.Direction(body, cycle, clock.TickInDay);
        if (body is null || offset is null)
        {
            if (isSun) _sunActive = false;
            else _moonActive = false;
            orb.Visible = false;
            return;
        }

        var direction = new Vector3(offset.Value.X, offset.Value.Y, offset.Value.Z);
        if (isSun)
        {
            _sunActive = true;
            _sunDirection = direction;
            _sunDistance = body.OrbitRadius;
        }
        else
        {
            _moonActive = true;
            _moonDirection = direction;
            _moonDistance = body.OrbitRadius;
        }

        orb.Visible = _cameraReady;
        orb.Scale = new Vector3(body.Size, body.Size, 1f);
        if (orb.MaterialOverride is StandardMaterial3D material)
            material.AlbedoColor = ToColor(body.Tint);
    }

    private static Color ToColor(DimensionColor color) =>
        new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, 1f);
}
