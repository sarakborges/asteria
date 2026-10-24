using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Converts Object-authored surface and shadow-reception policy to Godot
/// materials. Shared by primitive and imported GLB surfaces, so their
/// receivesShadow behavior cannot diverge. Models keep independent per-surface
/// textures, colors and cutoff while instances share the immutable mesh.
/// </summary>
internal static class WorldObjectMaterials
{
    private const string ObjectShader = "res://shaders/voxel_object.gdshader";

    public static ShaderMaterial CreateLit(
        ObjectDefinition definition, Texture2D texture,
        float alphaCutoff, Color? albedo = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(texture);
        // All world objects use an unshaded material; lighting and shadow overlays are disabled.
        var shader = ResourceLoader.Load<Shader>(ObjectShader) ??
            throw new FileNotFoundException($"Missing Object shader: {ObjectShader}");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("object_texture", texture);
        material.SetShaderParameter("alpha_cutoff", alphaCutoff);
        if (albedo is { } color)
            material.SetShaderParameter("model_albedo_color", color);
        return material;
    }

    public static StandardMaterial3D CreateUnlit(
        ObjectDefinition definition, Texture2D texture,
        float alphaCutoff, Color? albedo = null,
        bool preserveVertexColors = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(texture);
        var material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = albedo ?? Colors.White,
            VertexColorUseAsAlbedo = preserveVertexColors ||
                definition.Tint != BlockTint.None,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DisableReceiveShadows = true,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = alphaCutoff,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return material;
    }

}
