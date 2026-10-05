shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2DArray terrain_textures : source_color, filter_nearest, repeat_enable;

varying float voxel_face_shade;

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
    if (base_tint_enabled) {
        rgb = apply_layer_tint(rgb, COLOR.rgb);
    }

    float alpha = base.a;
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
        alpha = max(alpha, overlay.a);
    }

    ALBEDO = rgb * voxel_face_shade;
    ALPHA = alpha;
    ROUGHNESS = 1.0;
}
