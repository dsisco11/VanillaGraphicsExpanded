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

/** Implements render probe atlas filter delta debug for its explicit view entrypoint. */
vec4 renderProbeAtlasFilterDeltaDebug()
{
    vec3 curr = texture(probeAtlasCurrent, uv).rgb;
    vec3 filt = texture(probeAtlasFiltered, uv).rgb;
    float d = length(filt - curr);
    // Scale a bit so subtle changes show up.
    return vec4(heatmap(clamp(d * 4.0, 0.0, 1.0)), 1.0);
}

/** Renders only the ProbeAtlasFilterDelta view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasFilterDeltaDebug();
}
