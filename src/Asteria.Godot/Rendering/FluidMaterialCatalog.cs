using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class FluidMaterialCatalog
{
    private const string ShaderPath =
        "res://shaders/voxel_fluid.gdshader";

    private readonly Dictionary<FluidRuntimeId, ShaderMaterial>
        _materials;

    private FluidMaterialCatalog(
        Dictionary<FluidRuntimeId, ShaderMaterial> materials)
    {
        _materials = materials;
    }

    public Material Get(FluidRuntimeId fluid) =>
        _materials.TryGetValue(fluid, out var material)
            ? material
            : throw new KeyNotFoundException(
                $"Missing material for fluid {fluid.Value}.");

    public static FluidMaterialCatalog Create(
        FluidRegistry fluids,
        PackSelection selection)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        var shader =
            ResourceLoader.Load<Shader>(ShaderPath)
            ?? throw new FileNotFoundException(
                $"Missing voxel fluid shader: {ShaderPath}");
        var materials =
            new Dictionary<FluidRuntimeId, ShaderMaterial>();

        foreach (var (runtimeId, definition) in
                 fluids.AuthoredDefinitions())
        {
            var color = definition.Color;
            var material =
                new ShaderMaterial
                {
                    Shader = shader,
                };

            material.SetShaderParameter(
                "fluid_color",
                new Color(
                    color.Red / 255f,
                    color.Green / 255f,
                    color.Blue / 255f,
                    1f));
            material.SetShaderParameter(
                "fluid_opacity",
                definition.Opacity);
            material.SetShaderParameter(
                "fluid_roughness",
                definition.Roughness);

            if (definition.Texture is { } texturePath)
            {
                var resourcePath =
                    ProjectPackPaths.Resource(
                        selection,
                        texturePath);
                var texture =
                    ResourceLoader.Load<Texture2D>(
                        resourcePath)
                    ?? throw new FileNotFoundException(
                        $"Fluid texture was not imported by Godot: {resourcePath}");

                material.SetShaderParameter(
                    "fluid_texture",
                    texture);
                material.SetShaderParameter(
                    "use_fluid_texture",
                    true);
            }
            else
            {
                material.SetShaderParameter(
                    "use_fluid_texture",
                    false);
            }

            materials.Add(
                runtimeId,
                material);
        }

        return new FluidMaterialCatalog(
            materials);
    }
}
