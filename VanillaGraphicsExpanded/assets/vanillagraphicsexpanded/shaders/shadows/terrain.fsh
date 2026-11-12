#version 450 core
@import "../includes/shadows/fragment-interface.glsl"
@import "../includes/shadows/views.glsl"
@import "../includes/shadows/writing.glsl"
uniform sampler2D vge_shadowAtlas;
in vec2 vge_shadowUv;
out vec4 vge_materialCoverage;
/** Binary terrain coverage is evaluated before the virtual page writer runs. */
void main() {
    float coverage = texture(vge_shadowAtlas, vge_shadowUv).a;
    if (coverage < 0.02) discard;
    vge_materialCoverage = vec4(coverage);
}
