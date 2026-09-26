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


/** Implements render probe atlas gather input source debug for its explicit view entrypoint. */
vec4 renderProbeAtlasGatherInputSourceDebug()
{
    // Solid color to make it obvious what the renderer is feeding into gather.
    // Red = trace/raw, Yellow = temporal/current, Green = filtered
    if (gatherAtlasSource == 2) return vec4(0.1, 1.0, 0.1, 1.0);
    if (gatherAtlasSource == 1) return vec4(1.0, 1.0, 0.1, 1.0);
    return vec4(1.0, 0.1, 0.1, 1.0);
}

/** Renders only the ProbeAtlasGatherInputSource view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasGatherInputSourceDebug();
}
