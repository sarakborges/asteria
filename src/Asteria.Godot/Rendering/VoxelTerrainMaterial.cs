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
    private Vector3 _lastHeldPosition;
    private BlockLightEmission _lastHeldEmission;

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

    /// <summary>
    /// Dynamic held lighting is render-only: no changed voxel light, chunk
    /// remeshing, save revisions or worker snapshots on player movement.
    /// </summary>
    public void SetHeldLight(Vector3 position, BlockLightEmission emission)
    {
        if (_lastHeldPosition == position && _lastHeldEmission == emission)
            return;

        _lastHeldPosition = position;
        _lastHeldEmission = emission;
        var rgb = new Vector3(
            emission.Red / (float)VoxelLight.MaxLevel,
            emission.Green / (float)VoxelLight.MaxLevel,
            emission.Blue / (float)VoxelLight.MaxLevel);

        foreach (var material in _materials.Values)
        {
            material.SetShaderParameter("held_light_position", position);
            material.SetShaderParameter("held_light_rgb", rgb);
        }
    }

    public void ClearHeldLight() =>
        SetHeldLight(Vector3.Zero, default);

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
