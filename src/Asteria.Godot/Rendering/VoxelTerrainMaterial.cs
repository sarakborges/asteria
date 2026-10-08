using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class VoxelTerrainMaterialSet
{
    private const string OpaqueShaderPath =
        "res://shaders/voxel_terrain_opaque.gdshader";
    private const string OpaqueNoShadowShaderPath =
        "res://shaders/voxel_terrain_opaque_noshadow.gdshader";
    private const string CutoutShaderPath =
        "res://shaders/voxel_terrain_cutout.gdshader";
    private const string CutoutNoShadowShaderPath =
        "res://shaders/voxel_terrain_cutout_noshadow.gdshader";
    private const string TranslucentShaderPath =
        "res://shaders/voxel_terrain_translucent.gdshader";
    private const string TranslucentNoShadowShaderPath =
        "res://shaders/voxel_terrain_translucent_noshadow.gdshader";

    private readonly Dictionary<TerrainRenderBatch, ShaderMaterial>
        _materials;

    private VoxelTerrainMaterialSet(
        Dictionary<TerrainRenderBatch, ShaderMaterial> materials)
    {
        _materials = materials;
    }

    public Material Get(TerrainRenderBatch batch) =>
        _materials.TryGetValue(batch, out var material)
            ? material
            : throw new KeyNotFoundException(
                $"Missing terrain material for {batch}.");

    public void SetWind(DimensionWindDefinition wind)
    {
        var velocity = wind.Velocity;
        var authored = new Vector2(velocity.X, velocity.Y);
        foreach (var material in _materials.Values)
            material.SetShaderParameter("wind_velocity", authored);
    }

    public static VoxelTerrainMaterialSet Create(
        TerrainTextureCatalog textures)
    {
        ArgumentNullException.ThrowIfNull(textures);

        var materials =
            new Dictionary<TerrainRenderBatch, ShaderMaterial>();

        foreach (var renderMode in Enum.GetValues<BlockRenderMode>())
        {
            foreach (var castsShadow in new[] { true, false })
            {
                var batch =
                    new TerrainRenderBatch(
                        renderMode,
                        castsShadow);
                materials.Add(
                    batch,
                    CreateMaterial(
                        ShaderPath(
                            renderMode,
                            castsShadow),
                        textures));
            }
        }

        return new VoxelTerrainMaterialSet(materials);
    }

    private static string ShaderPath(
        BlockRenderMode renderMode,
        bool castsShadow) =>
        (renderMode, castsShadow) switch
        {
            (BlockRenderMode.Opaque, true) =>
                OpaqueShaderPath,
            (BlockRenderMode.Opaque, false) =>
                OpaqueNoShadowShaderPath,
            (BlockRenderMode.Cutout, true) =>
                CutoutShaderPath,
            (BlockRenderMode.Cutout, false) =>
                CutoutNoShadowShaderPath,
            (BlockRenderMode.Translucent, true) =>
                TranslucentShaderPath,
            (BlockRenderMode.Translucent, false) =>
                TranslucentNoShadowShaderPath,
            _ => throw new ArgumentOutOfRangeException(
                nameof(renderMode)),
        };

    private static ShaderMaterial CreateMaterial(
        string shaderPath,
        TerrainTextureCatalog textures)
    {
        var shader =
            ResourceLoader.Load<Shader>(shaderPath)
            ?? throw new FileNotFoundException(
                $"Missing voxel terrain shader: {shaderPath}");

        var material = new ShaderMaterial
        {
            Shader = shader,
        };

        material.SetShaderParameter(
            "terrain_textures",
            textures.TextureArray);

        return material;
    }
}
