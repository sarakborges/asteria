shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;

uniform vec4 fluid_color : source_color = vec4(0.31, 0.62, 0.84, 1.0);
uniform float fluid_opacity : hint_range(0.0, 1.0) = 0.72;
uniform float fluid_roughness : hint_range(0.0, 1.0) = 0.15;

const float AMBIENT_FLOOR = 0.055;
const float SKY_LIGHT_GAMMA = 1.35;
const float BLOCK_LIGHT_INTENSITY_GAMMA = 0.50;
const float BLOCK_LIGHT_COLOR_STRENGTH = 0.72;

varying float voxel_face_shade;
varying vec4 voxel_light;

void vertex() {
    voxel_light = CUSTOM0;

    vec3 normal = normalize(NORMAL);
    vec3 axis = abs(normal);

    if (axis.y >= axis.x && axis.y >= axis.z) {
        voxel_face_shade = normal.y >= 0.0 ? 1.0 : 0.5;
    } else if (axis.z >= axis.x) {
        voxel_face_shade = 0.8;
    } else {
        voxel_face_shade = 0.6;
    }
}

void fragment() {
    float sky_level = clamp(voxel_light.r, 0.0, 1.0);
    vec3 block_levels = clamp(voxel_light.gba, vec3(0.0), vec3(1.0));
    float ambient_occlusion = clamp(COLOR.a, 0.0, 1.0);

    float sky_local_light = mix(
        AMBIENT_FLOOR,
        1.0,
        pow(sky_level, SKY_LIGHT_GAMMA)
    );

    float block_peak = max(max(block_levels.r, block_levels.g), block_levels.b);
    vec3 block_hue = block_levels / max(block_peak, 0.001);
    float block_intensity = pow(block_peak, BLOCK_LIGHT_INTENSITY_GAMMA);

    float combined_intensity =
        1.0 - (1.0 - sky_local_light) * (1.0 - block_intensity);
    float block_hue_weight = smoothstep(0.08, 0.55, block_intensity);
    float color_weight = block_hue_weight * BLOCK_LIGHT_COLOR_STRENGTH;
    vec3 combined_hue = mix(vec3(1.0), block_hue, color_weight);

    vec3 local_light =
        combined_hue *
        combined_intensity *
        ambient_occlusion *
        voxel_face_shade;

    ALBEDO = fluid_color.rgb * local_light;
    ALPHA = fluid_opacity;
    ROUGHNESS = fluid_roughness;
}
