// A single transient RGB light carried by the player. Its source comes
// from the selected block's authored emission. No remesh or persisted voxel
// illumination is needed for camera movement.
uniform vec3 held_light_position = vec3(0.0, 0.0, 0.0);
uniform vec3 held_light_rgb = vec3(0.0, 0.0, 0.0);
varying vec3 held_light_world_position;

vec3 sample_held_dynamic_light(vec3 world_position) {
    if (dot(held_light_rgb, held_light_rgb) < 0.000001) {
        return vec3(0.0);
    }
    float separation = distance(world_position, held_light_position);
    float proximity = clamp(1.0 - separation / 8.0, 0.0, 1.0);
    return held_light_rgb * proximity * proximity;
}
