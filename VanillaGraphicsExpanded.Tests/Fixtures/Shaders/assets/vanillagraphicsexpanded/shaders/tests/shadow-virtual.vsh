#version 450 core
#pragma vge_shadow_interface
@import "../includes/shadows/views.glsl"

vec4 vge_shadowPosition;

void main()
{
    vec2 positions[3] = vec2[3](vec2(0.0, 0.0), vec2(16.0, 0.0), vec2(0.0, 16.0));
    vec2 deformedPosition = positions[gl_VertexID];
    deformedPosition.x += 0.25 * float(gl_VertexID);
    vge_shadowPosition = vec4(deformedPosition, 0.0, 1.0);
    if (vge_shadowObject == 1u)
        return;
}
