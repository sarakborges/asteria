shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;

uniform vec4 underwater_tint : source_color =
    vec4(0.20, 0.52, 0.72, 0.24);

void fragment() {
    ALBEDO = underwater_tint.rgb;
    ALPHA = underwater_tint.a;
    ROUGHNESS = 1.0;
}
