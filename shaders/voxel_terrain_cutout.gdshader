shader_type spatial;
render_mode unshaded, cull_back, depth_prepass_alpha;

uniform sampler2DArray terrain_textures : source_color, filter_nearest, repeat_enable;

const float AMBIENT_FLOOR = 0.055;
const float SKY_LIGHT_GAMMA = 1.35;
const float BLOCK_LIGHT_INTENSITY_GAMMA = 0.50;
const float BLOCK_LIGHT_COLOR_STRENGTH = 0.72;

varying float voxel_face_shade;
varying vec4 voxel_light;

vec3 apply_layer_tint(vec3 source, vec3 tint) {
    const vec3 weights = vec3(0.2126, 0.7152, 0.0722);
    float source_luma = max(dot(source, weights), 0.0);
    float tint_peak = max(max(tint.r, tint.g), max(tint.b, 0.001));
    vec3 hue = tint / tint_peak;
    float hue_luma = max(dot(hue, weights), 0.001);
    float compensation = min(2.0, 1.0 / hue_luma);
    return clamp(vec3(source_luma) * hue * compensation * 1.08, vec3(0.0), vec3(1.0));
}

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
    vec2 tiled_uv = fract(UV);

    float base_code = UV2.x;
    int base_index = int(floor(base_code + 0.001));
    bool base_tint_enabled = fract(base_code) > 0.1;

    vec4 base = texture(
        terrain_textures,
        vec3(tiled_uv, float(base_index))
    );

    vec3 rgb = base.rgb;
    float alpha = base.a;

    if (base_tint_enabled) {
        rgb = apply_layer_tint(rgb, COLOR.rgb);
    }

    if (UV2.y >= 0.0) {
        float overlay_code = UV2.y;
        int overlay_index = int(floor(overlay_code + 0.001));
        bool overlay_tint_enabled = fract(overlay_code) > 0.1;

        vec4 overlay = texture(
            terrain_textures,
            vec3(tiled_uv, float(overlay_index))
        );
        vec3 overlay_rgb = overlay.rgb;

        if (overlay_tint_enabled) {
            overlay_rgb = apply_layer_tint(overlay_rgb, COLOR.rgb);
        }

        rgb = mix(rgb, overlay_rgb, overlay.a);
        alpha = alpha + overlay.a * (1.0 - alpha);
    }

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

    ALBEDO = rgb * local_light;
    ROUGHNESS = 1.0;
    ALPHA = alpha;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}
