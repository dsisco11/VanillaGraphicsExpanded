#version 450 core
@import "../includes/shadows/vertex-interface.glsl"
@import "../includes/shadows/views.glsl"
@import "../includes/shadows/terrain-parameters.glsl"
layout(location=0) in vec3 vge_vertexPosition;
layout(location=1) in vec2 vge_vertexUv;
layout(location=3) in int vge_vertexFlags;
#if VGE_OBJECT_INSTANCED
layout(location=6) in mat4 shadowInstanceMatrix;
#endif
layout(std140) uniform VgeShadowObjectFlags { int vge_additionalVertexFlags; int vge_deformationMode; };
out vec2 vge_shadowUv;
out vec4 vge_shadowPosition;
/** Retains the native instance transform before shared object deformation. */
void main() {
    vec4 local = vec4(vge_vertexPosition, 1.0);
#if VGE_OBJECT_INSTANCED
    local = shadowInstanceMatrix * local;
#endif
    vec4 vge_materialPosition = vge_materialTransform * local;
    if (vge_deformationMode == 0 || vge_deformationMode == 2) {
        vec4 moved = VgeVertexDeform(VgeShadowDeformationInputs(), vge_vertexFlags | vge_additionalVertexFlags, vge_materialPosition);
        vge_materialPosition = VgeGlobalDeform(VgeShadowDeformationInputs(), mix(vge_materialPosition, moved, vge_deformationMode == 2 ? 0.25 : 1.0));
    }
    vge_shadowUv = vge_vertexUv;
    vge_shadowPosition = vge_shadowOriginTransform * vge_materialPosition;
}
