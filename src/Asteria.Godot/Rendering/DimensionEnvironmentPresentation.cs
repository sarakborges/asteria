using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;
using GEnvironment = Godot.Environment;

namespace Asteria.Client.Rendering;

/// <summary>
/// Godot-only presentation of the dimension clock. Voxel skylight and terrain
/// shaders are deliberately not modified by the day/night cycle.
/// </summary>
public sealed class DimensionEnvironmentPresentation
{
    private readonly Node _parent;
    private WorldEnvironment? _node;
    private GEnvironment? _environment;
    private DirectionalLight3D? _sunLight;
    private MeshInstance3D? _sunOrb;
    private MeshInstance3D? _moonOrb;
    private DimensionEnvironmentDefinition? _definition;
    private Vector3 _sunDirection;
    private Vector3 _moonDirection;
    private float _sunDistance;
    private float _moonDistance;
    private bool _cameraReady;
    private bool _sunActive;
    private bool _moonActive;

    public DimensionEnvironmentPresentation(Node parent)
    {
        _parent = parent ??
            throw new ArgumentNullException(nameof(parent));
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

        _definition = dimension.Environment;
        _cameraReady = false;
        _environment ??= new GEnvironment
        {
            BackgroundMode = GEnvironment.BGMode.Color,
            AmbientLightSource = GEnvironment.AmbientSource.Color,
        };

        if (_node is null)
        {
            _node = new WorldEnvironment
            {
                Name = "DimensionEnvironment",
            };
            _parent.AddChild(_node);
            _node.Environment = _environment;
        }

        _sunLight ??= CreateSunLight();
        _sunOrb ??= CreateOrb("DaySun");
        _moonOrb ??= CreateOrb("NightMoon");
        ConfigureOrb(_sunOrb, cycle.Sun, selection);
        ConfigureOrb(_moonOrb, cycle.Moon, selection);
        Update(cycle, clock);
    }

    /// <summary>Discard session presentation on return to the main menu.</summary>
    public void Clear()
    {
        _node?.QueueFree();
        _sunLight?.QueueFree();
        _sunOrb?.QueueFree();
        _moonOrb?.QueueFree();
        _node = null;
        _environment = null;
        _sunLight = null;
        _sunOrb = null;
        _moonOrb = null;
        _definition = null;
        _cameraReady = false;
        _sunActive = false;
        _moonActive = false;
    }

    public void Update(
        DayNightCycleDefinition cycle,
        DayNightClock clock)
    {
        if (_environment is null ||
            _definition is null)
        {
            return;
        }

        var sample = clock.Sample;
        // The authored voxel skylight remains unchanged; only the engine
        // ambient, background, fog and celestials follow this factor.
        var factor = Math.Clamp(
            sample.SkyLightFactor, 0f, 1f);
        var backgroundFactor = 0.22f + 0.78f * factor;
        var fogFactor = 0.40f + 0.60f * factor;

        _environment.BackgroundColor =
            ScaledColor(_definition.BackgroundColor, backgroundFactor);
        _environment.AmbientLightColor =
            ToColor(_definition.AmbientColor);
        _environment.AmbientLightEnergy =
            _definition.AmbientEnergy * factor;
        _environment.FogEnabled =
            _definition.FogDensity > 0f;
        _environment.FogLightColor =
            ScaledColor(_definition.FogColor, fogFactor);
        _environment.FogDensity =
            _definition.FogDensity;

        ApplyCelestial(
            _sunOrb!,
            cycle.Sun,
            cycle,
            clock,
            isSun: true);
        ApplyCelestial(
            _moonOrb!,
            cycle.Moon,
            cycle,
            clock,
            isSun: false);
    }

    public void FollowCamera(Vector3 position)
    {
        _cameraReady = true;
        if (_sunOrb is not null)
        {
            _sunOrb.Visible = _sunActive;
            if (_sunActive)
            {
                _sunOrb.GlobalPosition =
                    position + _sunDirection * _sunDistance;
            }
        }

        if (_moonOrb is not null)
        {
            _moonOrb.Visible = _moonActive;
            if (_moonActive)
            {
                _moonOrb.GlobalPosition =
                    position + _moonDirection * _moonDistance;
            }
        }
    }

    private DirectionalLight3D CreateSunLight()
    {
        var light = new DirectionalLight3D
        {
            Name = "DimensionSunLight",
            ShadowEnabled = false,
        };
        _parent.AddChild(light);
        return light;
    }

    private MeshInstance3D CreateOrb(string name)
    {
        // MineClone's celestial PNGs are flat, camera-facing sky sprites.
        // Use an unshaded quad rather than a sphere, which distorts the art.
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
        // Load once per Sphere activation, never on the world tick.
        // Clearing the texture also handles a subsequent Sphere with no art.
        if (orb.MaterialOverride is StandardMaterial3D material)
        {
            material.AlbedoTexture = body?.Texture is { } texture
                ? ProjectPackFiles.LoadTexture(selection, texture)
                : null;
        }
    }

    private void ApplyCelestial(
        MeshInstance3D orb,
        DayNightCelestialDefinition? body,
        DayNightCycleDefinition cycle,
        DayNightClock clock,
        bool isSun)
    {
        var progress = body is null
            ? null
            : cycle.ProgressBetweenPhases(
                clock.TickInDay,
                body.RisePhase,
                body.SetPhase);

        if (body is null || progress is null)
        {
            if (isSun) _sunActive = false;
            else _moonActive = false;
            orb.Visible = false;
            if (isSun && _sunLight is not null)
            {
                _sunLight.Visible = false;
            }
            return;
        }

        var t = (float)progress.Value;
        var azimuth = Mathf.DegToRad(
            Mathf.Lerp(
                body.RiseAzimuthDegrees,
                body.SetAzimuthDegrees,
                t));
        var altitude = Mathf.DegToRad(
            Mathf.Sin(t * Mathf.Pi) *
            body.MaxAltitudeDegrees);
        var horizontal = Mathf.Cos(altitude);
        var direction = new Vector3(
            horizontal * Mathf.Sin(azimuth),
            Mathf.Sin(altitude),
            horizontal * Mathf.Cos(azimuth));

        if (isSun) _sunActive = true;
        else _moonActive = true;
        orb.Visible = _cameraReady;
        orb.Scale = new Vector3(body.Size, body.Size, 1f);
        if (orb.MaterialOverride is StandardMaterial3D material)
        {
            material.AlbedoColor = ToColor(body.Tint);
        }

        if (isSun)
        {
            _sunDirection = direction;
            _sunDistance = body.OrbitRadius;
            _sunLight!.Visible = true;
            _sunLight.LightColor = ToColor(body.Tint);
            _sunLight.LightEnergy =
                1.0f * clock.Sample.SkyLightFactor;
            _sunLight.LookAt(-direction, Vector3.Up);
        }
        else
        {
            _moonDirection = direction;
            _moonDistance = body.OrbitRadius;
        }
    }

    private static Color ToColor(DimensionColor color) =>
        new(
            color.Red / 255f,
            color.Green / 255f,
            color.Blue / 255f,
            1f);

    private static Color ScaledColor(
        DimensionColor color,
        float factor) =>
        new(
            color.Red / 255f * factor,
            color.Green / 255f * factor,
            color.Blue / 255f * factor,
            1f);
}
