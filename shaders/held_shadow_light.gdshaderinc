// Receive RGB from the single camera-held Godot OmniLight. Its
// ATTENUATION contains real shadow-map visibility, unlike the prior
// distance-only shader that brightened walls from behind.
varying float held_surface_shade;

void light() {
    // The authored voxel sky/lighting already contributes via EMISSION.
    // Do not apply the scene's sunlight a second time to terrain.
    if (!LIGHT_IS_DIRECTIONAL) {
        DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION *
            held_surface_shade / PI;
    }
}
