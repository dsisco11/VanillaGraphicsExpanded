#version 450 core
#define VGE_VERTEX_DEFORMATION_ENABLED 1
layout(std140, binding=28) uniform DeformationInputs {
    vec3 worldOrigin; float animationTime;
    float windPhase; float windDetailPhase; float liquidPhase; float windSpeed;
    float worldWarpStrength; float glitchStrength; float windStrength;
    float liquidStrength; int perceptionMode; float perceptionStrength; float padding0; float padding1;
};
@import "vanillagraphicsexpanded:shaders/includes/deformation/vertex-deformation.glsl"
layout(local_size_x=1) in;
layout(std430, binding=0) buffer DeformationResults { vec4 positions[4]; } results;
/** Runs actual static warp modes and entity liquid selection after nonidentity object placement. */
void main() {
    mat4 placement = mat4(1.0);
    placement[3].xyz = vec3(1.0, -0.5, 2.0);
    vec4 source = placement * vec4(2.25, -1.5, 4.75, 1.0);
    VgeVertexDeformationInputs inputs = VgeVertexDeformationInputs(worldOrigin, animationTime,
        windPhase, windDetailPhase, liquidPhase, windSpeed, worldWarpStrength,
        glitchStrength, windStrength, liquidStrength, perceptionMode, perceptionStrength);
    int foliageFlags = int((4u << 25u) | (6u << 29u));
    vec4 moved = VgeVertexDeform(inputs, foliageFlags, source);
    // Match the native static material's dontWarpVertices selection exactly.
    results.positions[0] = VgeGlobalDeform(inputs, moved);                  // mode 0: full warp
    results.positions[1] = source;                                  // mode 1: no warp
    results.positions[2] = VgeGlobalDeform(inputs, mix(source, moved, 0.25)); // mode 2: quarter-strength warp
    int entityFlags = int(6u << 25u);
    vec4 entity = (entityFlags & int(6u << 25u)) != 0
        ? VgeLiquidDeform(inputs, true, source, 5.0) : VgeVertexDeform(inputs, entityFlags, source);
    results.positions[3] = VgeGlobalDeform(inputs, entity);
}
