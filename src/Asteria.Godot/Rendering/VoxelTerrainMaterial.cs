using Godot;

namespace Asteria.Client.Rendering;

public static class VoxelTerrainMaterial
{
    private const string ShaderPath = "res://shaders/voxel_terrain.gdshader";

    public static ShaderMaterial Create(TerrainTextureCatalog textures)
    {
        ArgumentNullException.ThrowIfNull(textures);

        var shader = ResourceLoader.Load<Shader>(ShaderPath)
            ?? throw new FileNotFoundException($"Missing voxel terrain shader: {ShaderPath}");

        var material = new ShaderMaterial
        {
            Shader = shader,
        };
        material.SetShaderParameter("terrain_textures", textures.TextureArray);
        return material;
    }
}
