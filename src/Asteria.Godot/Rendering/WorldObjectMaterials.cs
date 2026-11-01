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
    private const string ShadowOverlayShader = "res://shaders/voxel_object_shadow_overlay.gdshader";
    internal const string SunShadowGlobal = "object_sun_shadow_strength";

    public static ShaderMaterial CreateLit(
        ObjectDefinition definition, Texture2D texture,
        float alphaCutoff, Color? albedo = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(texture);
        // Voxel light remains authoritative for the base color; authored
        // sunlight shadow reception is a separate, transparent overlay.
        var shader = ResourceLoader.Load<Shader>(ObjectShader) ??
            throw new FileNotFoundException($"Missing Object shader: {ObjectShader}");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("object_texture", texture);
        material.SetShaderParameter("alpha_cutoff", alphaCutoff);
        if (albedo is { } color)
            material.SetShaderParameter("model_albedo_color", color);
        if (definition.ReceivesShadow)
            material.NextPass = CreateShadowOverlay(texture, alphaCutoff, albedo?.A ?? 1f);
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
            // Independent of castsShadow (GeometryInstance3D-owned).
            DisableReceiveShadows = !definition.ReceivesShadow,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = alphaCutoff,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (definition.ReceivesShadow)
            material.NextPass = CreateShadowOverlay(texture, alphaCutoff, albedo?.A ?? 1f);
        return material;
    }

    private static ShaderMaterial CreateShadowOverlay(Texture2D texture,
        float alphaCutoff, float alphaMultiplier)
    {
        var shader = ResourceLoader.Load<Shader>(ShadowOverlayShader) ??
            throw new FileNotFoundException($"Missing Object shadow shader: {ShadowOverlayShader}");
        var pass = new ShaderMaterial { Shader = shader };
        pass.SetShaderParameter("object_texture", texture);
        pass.SetShaderParameter("alpha_cutoff", alphaCutoff);
        pass.SetShaderParameter("alpha_multiplier", alphaMultiplier);
        return pass;
    }
}
