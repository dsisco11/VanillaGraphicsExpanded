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


/** Implements render probe atlas trace outcome debug for its explicit view entrypoint. */
vec4 renderProbeAtlasTraceOutcomeDebug()
{
    ivec2 size = textureSize(probeAtlasMeta, 0);
    ivec2 coord = clamp(ivec2(uv * vec2(size)), ivec2(0), size - 1);
    uint flags = floatBitsToUint(texelFetch(probeAtlasMeta, coord, 0).y);
    uint outcome = (flags & LUMON_META_OUTCOME_MASK) >> LUMON_META_OUTCOME_SHIFT;
    if (outcome == 1u) return vec4(1, 0, 0, 1);
    if (outcome == 2u) return vec4(0, 1, 0, 1);
    if (outcome == 3u) return vec4(1, 1, 0, 1);
    if (outcome == 4u) return vec4(1, 0, 1, 1);
    if (outcome == 5u) return vec4(0, 1, 1, 1);
    if (outcome == 6u) return vec4(0.25, 0.5, 1, 1);
    return vec4(0, 0, 0, 1);
}

/** Renders only the ProbeAtlasTraceOutcome view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasTraceOutcomeDebug();
}
