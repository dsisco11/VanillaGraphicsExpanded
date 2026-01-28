#version 330 core

vec2 uv;

out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_worldprobe.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"

@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/lumon_debug_common.glsl"
@import "./includes/lumon_debug_probe_anchors.glsl"

void main(void)
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = RenderDebug_ProbeAnchors(screenPos);
}
