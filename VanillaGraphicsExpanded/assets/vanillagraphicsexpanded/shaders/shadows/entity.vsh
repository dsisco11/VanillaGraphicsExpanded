#version 450 core
@import "../includes/shadows/vertex-interface.glsl"
@import "../includes/shadows/views.glsl"
@import "../includes/shadows/terrain-parameters.glsl"
layout(location=0) in vec3 vge_vertexPosition;
layout(location=1) in vec2 vge_vertexUv;
layout(location=3) in int vge_vertexFlags;
layout(location=5) in int vge_jointIndex;
layout(std140) uniform VgeShadowAnimation { mat4 jointTransforms[230]; } vge_shadowAnimation;
layout(std140) uniform VgeShadowEntityFlags { int vge_additionalVertexFlags; };
out vec2 vge_shadowUv;
out vec4 vge_shadowPosition;
/** Applies the captured joint palette before object placement and shared material motion. */
void main() {
    vec4 vge_materialPosition = vge_materialTransform * vge_shadowAnimation.jointTransforms[clamp(vge_jointIndex, 0, 229)] * vec4(vge_vertexPosition, 1.0);
    int materialFlags = (vge_vertexFlags | vge_additionalVertexFlags) & ~(8 << 25);
    if ((materialFlags & (6 << 25)) != 0) vge_materialPosition = VgeLiquidDeform(VgeShadowDeformationInputs(), true, vge_materialPosition, 5.0);
    else vge_materialPosition = VgeVertexDeform(VgeShadowDeformationInputs(), materialFlags, vge_materialPosition);
    vge_materialPosition = VgeGlobalDeform(VgeShadowDeformationInputs(), vge_materialPosition);
    vge_shadowUv = vge_vertexUv;
    vge_shadowPosition = vge_shadowOriginTransform * vge_materialPosition;
}
