using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class UnderwaterViewPresentation
{
    private const float OverlayDistance = 0.10f;
    private const float MinimumStrength = 0.16f;
    private const float MaximumStrength = 0.34f;

    private readonly FluidRegistry _fluids;
    private readonly MeshInstance3D _overlay;
    private readonly ShaderMaterial _material;

    public UnderwaterViewPresentation(
        Camera3D camera,
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _fluids =
            fluids ??
            throw new ArgumentNullException(nameof(fluids));

        var shader =
            GD.Load<Shader>(
                "res://shaders/underwater_overlay.gdshader") ??
            throw new InvalidOperationException(
                "Could not load underwater overlay shader.");

        _material =
            new ShaderMaterial
            {
                Shader = shader,
                RenderPriority = 127,
            };

        var quad =
            new QuadMesh
            {
                Size =
                    new Vector2(
                        2f,
                        2f),
                Material =
                    _material,
            };

        _overlay =
            new MeshInstance3D
            {
                Name =
                    "UnderwaterOverlay",
                Mesh =
                    quad,
                Position =
                    new Vector3(
                        0f,
                        0f,
                        -OverlayDistance),
                Visible =
                    false,
                CastShadow =
                    GeometryInstance3D
                        .ShadowCastingSetting
                        .Off,
            };

        camera.AddChild(
            _overlay);
    }

    public void Apply(
        FluidBodyContact contact)
    {
        if (!contact.EyeSubmerged ||
            contact.Fluid.IsNone)
        {
            _overlay.Visible = false;
            return;
        }

        var definition =
            _fluids.GetDefinition(
                contact.Fluid);
        var color =
            definition.Color;
        var strength =
            Mathf.Clamp(
                MinimumStrength +
                definition.Opacity *
                0.18f,
                MinimumStrength,
                MaximumStrength);

        _material.SetShaderParameter(
            "underwater_tint",
            new Color(
                color.Red / 255f,
                color.Green / 255f,
                color.Blue / 255f,
                strength));

        _overlay.Visible = true;
    }

    public void Retire()
    {
        _overlay.QueueFree();
    }
}
