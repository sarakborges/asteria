uniform sampler2D object_texture : source_color, filter_nearest, repeat_disable;
uniform float alpha_cutoff = 0.5;
uniform bool preview_mode = false;
uniform vec3 preview_tint : source_color = vec3(1.0);
uniform vec4 model_albedo_color : source_color = vec4(1.0);

varying vec3 instance_tint;

void vertex() {
    instance_tint = COLOR.rgb;
}

void fragment() {
    vec4 texel = texture(object_texture, UV);
    ALBEDO = texel.rgb * model_albedo_color.rgb * instance_tint * preview_tint;
    ROUGHNESS = 1.0;
    ALPHA = texel.a * model_albedo_color.a;
    ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;
}
