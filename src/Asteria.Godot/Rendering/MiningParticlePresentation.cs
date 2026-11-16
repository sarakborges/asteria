using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One MultiMesh for every active Sphere. Per-instance custom data encodes
/// the selected authored base/overlay atlas indices; per-instance color holds
/// biome/dye tint. No per-material draw calls or per-chip scene nodes.
/// </summary>
internal sealed class MiningParticlePresentation
{
    private const string ChipShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, blend_mix;
        uniform sampler2DArray terrain_textures : source_color, filter_nearest, repeat_enable;
        varying vec2 chip_layers;
        varying vec3 chip_tint;
        varying float chip_seed;

        void vertex() {
            chip_layers = INSTANCE_CUSTOM.xy;
            chip_tint = COLOR.rgb;
            chip_seed = INSTANCE_CUSTOM.z;
        }

        void fragment() {
            // Textureless content keeps the authored neutral chip color.
            vec3 result_rgb = vec3(0.73, 0.70, 0.64);
            float opacity = 1.0;

            if (abs(chip_layers.x) >= 0.5) {
                // Sample a different pixel patch for each fragment instead of
                // squeezing an entire 64px face across a 3-8cm particle.
                vec2 offset = vec2(
                    fract(chip_seed * 0.61803398875),
                    fract(chip_seed * 0.41421356237));
                vec2 sample_uv = fract(UV * 0.25 + offset);
                float base_layer = abs(chip_layers.x) - 1.0;
                vec4 base = texture(terrain_textures,
                    vec3(sample_uv, base_layer));
                vec3 base_rgb = base.rgb *
                    (chip_layers.x < 0.0 ? chip_tint : vec3(1.0));
                result_rgb = base_rgb;
                opacity = base.a;

                if (abs(chip_layers.y) >= 0.5) {
                    vec4 overlay = texture(terrain_textures,
                        vec3(sample_uv, abs(chip_layers.y) - 1.0));
                    vec3 upper_rgb = overlay.rgb *
                        (chip_layers.y < 0.0 ? chip_tint : vec3(1.0));
                    result_rgb = mix(base_rgb, upper_rgb, overlay.a);
                    opacity = max(base.a, overlay.a);
                }
            }

            ALBEDO = result_rgb;
            ALPHA = opacity;
            ALPHA_SCISSOR_THRESHOLD = 0.1;
        }
        """;

    private readonly MultiMesh _batch;
    private readonly MultiMeshInstance3D _visual;
    private readonly TerrainTextureLookup _textures;
    private readonly Dictionary<MiningFragmentAppearance, (float Base, float Overlay)>
        _encoded = [];

    public MiningParticlePresentation(
        Node3D parent, TerrainTextureLookup textures,
        VoxelTerrainMaterialSet terrainMaterials)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        ArgumentNullException.ThrowIfNull(terrainMaterials);
        var material = new ShaderMaterial
        {
            Shader = new Shader { Code = ChipShader },
        };
        material.SetShaderParameter("terrain_textures", terrainMaterials.TextureArray);
        _batch = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            InstanceCount = MiningParticleRuntime.MaximumActive,
            VisibleInstanceCount = 0,
        };
        _visual = new MultiMeshInstance3D
        {
            Name = "MiningChips",
            Multimesh = _batch,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(_visual);
    }

    public void Sync(MiningParticleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var count = Math.Min(runtime.Particles.Count, MiningParticleRuntime.MaximumActive);
        if (count == 0)
        {
            _batch.VisibleInstanceCount = 0;
            return;
        }

        var min = new Vector3(float.PositiveInfinity,
            float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity,
            float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < count; ++i)
        {
            var particle = runtime.Particles[i];
            var size = particle.Scale;
            var position = new Vector3(
                particle.Position.X, particle.Position.Y, particle.Position.Z);
            _batch.SetInstanceTransform(i, new Transform3D(
                Basis.Identity.Scaled(new Vector3(size, size, size)), position));
            var (baseLayer, overlayLayer) = Encode(particle.Appearance);
            _batch.SetInstanceCustomData(i,
                new Color(baseLayer, overlayLayer, particle.PatchSeed, 0f));
            _batch.SetInstanceColor(i, new Color(
                particle.Tint.X, particle.Tint.Y, particle.Tint.Z, 1f));
            var extent = Vector3.One * size;
            min = min.Min(position - extent);
            max = max.Max(position + extent);
        }
        _visual.CustomAabb = new Aabb(min, max - min);
        _batch.VisibleInstanceCount = count;
    }

    private (float Base, float Overlay) Encode(
        MiningFragmentAppearance? appearance)
    {
        if (appearance is null || appearance.Textures.Count == 0)
            return default;
        if (_encoded.TryGetValue(appearance, out var cached))
            return cached;

        // Mirrors ChunkMeshDataBuilder's two intrinsic layers per face.
        if (appearance.Textures.Count > 2)
            throw new InvalidOperationException(
                "Mining chips require at most two authored face textures.");
        var bottom = EncodeLayer(appearance, 0);
        var overlay = appearance.Textures.Count > 1
            ? EncodeLayer(appearance, 1) : 0f;
        if (_encoded.Count >= 256)
            _encoded.Clear();
        var encoded = (bottom, overlay);
        _encoded.Add(appearance, encoded);
        return encoded;
    }

    private float EncodeLayer(MiningFragmentAppearance appearance, int index)
    {
        var layer = appearance.Textures[index];
        var code = _textures.GetIndex(layer.Texture) + 1f;
        var colored = layer.Dyable &&
            (appearance.Tint != BlockTint.None || appearance.DyeId is not null);
        return colored ? -code : code;
    }

    public void Clear()
    {
        _batch.VisibleInstanceCount = 0;
        _encoded.Clear();
    }
}
