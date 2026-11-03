#version 430 core
#define VGE_TESS_SHADOW 1
@import "../includes/tests/displacement_inputs.glsl"
@import "../includes/tessellation/terrain_displacement.glsl"

out vec4 result;
/** Captures directed and far shadow edge levels before world-frame publication. */
void main() {
    vec3 a = vec3(-.5, 0, -2), b = vec3(.5, 0, -2);
    result = vec4(VgeEdgeLevel(a, b), VgeEdgeLevel(b, a),
        VgeEdgeLevel(vec3(0, 0, -30), vec3(1, 0, -30)), 1);
}
