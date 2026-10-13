shader_type spatial;
// Shadow-to-opacity overlays only the cast-shadow attenuation; the first
// material pass preserves all existing authored voxel lighting and tint.
render_mode blend_mix, cull_disabled, depth_draw_never,
    specular_disabled, ambient_light_disabled, shadow_to_opacity;

uniform sampler2D object_texture : source_color, filter_nearest, repeat_disable;
uniform float alpha_cutoff = 0.5;
uniform float alpha_multiplier = 1.0;
global uniform float object_sun_shadow_strength;

void fragment() {
    float source_alpha = texture(object_texture, UV).a * alpha_multiplier;
    if (source_alpha < alpha_cutoff) {
        discard;
    }
    ALBEDO = vec3(0.05);
    ROUGHNESS = 1.0;
    // Shadow-to-opacity forces zero opacity in directly lit pixels.
    ALPHA = clamp(object_sun_shadow_strength, 0.0, 1.0) * 0.65;
}
