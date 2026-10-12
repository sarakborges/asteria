shader_type spatial;
render_mode unshaded, cull_disabled, depth_prepass_alpha;

uniform sampler2D object_texture : source_color, filter_nearest, repeat_disable;
uniform float alpha_cutoff = 0.5;
uniform bool preview_mode = false;
uniform vec3 preview_tint : source_color = vec3(1.0);
uniform vec4 model_albedo_color : source_color = vec4(1.0);

const float AMBIENT_FLOOR = 0.055;
const float SKY_LIGHT_GAMMA = 1.35;
const float BLOCK_LIGHT_INTENSITY_GAMMA = 0.50;
const float BLOCK_LIGHT_COLOR_STRENGTH = 0.72;

varying vec4 voxel_light;
varying vec3 instance_tint;
varying float object_face_shade;

void vertex() {
    voxel_light = preview_mode ? vec4(1.0) : INSTANCE_CUSTOM;
    instance_tint = COLOR.rgb;
    vec3 axis = abs(normalize(NORMAL));
    object_face_shade = axis.y >= axis.x && axis.y >= axis.z
        ? (NORMAL.y >= 0.0 ? 1.0 : 0.5)
        : (axis.z >= axis.x ? 0.8 : 0.6);
}

void fragment() {
    vec4 texel = texture(object_texture, UV);
    float sky_level = clamp(voxel_light.r, 0.0, 1.0);
    vec3 block_levels = clamp(voxel_light.gba, vec3(0.0), vec3(1.0));
    float sky_light = mix(AMBIENT_FLOOR, 1.0, pow(sky_level, SKY_LIGHT_GAMMA));
    float block_peak = max(max(block_levels.r, block_levels.g), block_levels.b);
    float block_intensity = pow(block_peak, BLOCK_LIGHT_INTENSITY_GAMMA);
    vec3 block_hue = block_levels / max(block_peak, 0.001);
    float total_intensity =
        1.0 - (1.0 - sky_light) * (1.0 - block_intensity);
    vec3 light_hue = mix(vec3(1.0), block_hue,
        smoothstep(0.08, 0.55, block_intensity) * BLOCK_LIGHT_COLOR_STRENGTH);

    ALBEDO = texel.rgb * model_albedo_color.rgb * instance_tint * preview_tint * light_hue *
        total_intensity * object_face_shade;
    ROUGHNESS = 1.0;
    ALPHA = texel.a * model_albedo_color.a;
    ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;
}
