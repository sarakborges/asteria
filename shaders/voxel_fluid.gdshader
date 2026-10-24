shader_type spatial;
render_mode unshaded, cull_back, blend_mix, depth_draw_never, shadows_disabled;

uniform vec4 fluid_color : source_color = vec4(0.31, 0.62, 0.84, 1.0);
uniform float fluid_opacity : hint_range(0.0, 1.0) = 0.72;
uniform float fluid_roughness : hint_range(0.0, 1.0) = 0.15;
uniform sampler2D fluid_texture : source_color, filter_nearest, repeat_enable;
uniform bool use_fluid_texture = false;

void fragment() {
    vec4 surface_sample = vec4(1.0);
    if (use_fluid_texture) {
        surface_sample = texture(fluid_texture, UV);
    }

    ALBEDO = fluid_color.rgb * surface_sample.rgb;
    ALPHA = fluid_opacity * surface_sample.a;
    ROUGHNESS = fluid_roughness;
}
