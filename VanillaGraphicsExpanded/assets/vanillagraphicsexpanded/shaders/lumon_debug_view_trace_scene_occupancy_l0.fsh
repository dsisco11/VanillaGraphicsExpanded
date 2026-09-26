#version 330 core

vec2 uv;
out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_near_field_scene.glsl"
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumon_frame_worldspace_bridge.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/trace_scene_debug_sample.glsl"



/** Renders only the TraceSceneOccupancyL0 view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = traceSceneDebugSample(screenPos, 56);
}
