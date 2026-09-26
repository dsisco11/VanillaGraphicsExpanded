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
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/heatmap.glsl"

/** Implements render probe atlas hit distance debug for its explicit view entrypoint. */
vec4 renderProbeAtlasHitDistanceDebug()
{
    // Atlas hit distance is stored in A as log(distance + 1).
    // Use the gather-input atlas since that's what downstream shading consumes.
    float distLog = texture(probeAtlasGatherInput, uv).a;
    // Scale log distances into [0,1] in a somewhat stable way.
    float t = clamp(distLog / 5.0, 0.0, 1.0);
    return vec4(heatmap(t), 1.0);
}

/** Renders only the ProbeAtlasHitDistance view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasHitDistanceDebug();
}
