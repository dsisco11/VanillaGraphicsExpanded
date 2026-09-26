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


/** Implements render probe atlas temporal alpha debug for its explicit view entrypoint. */
vec4 renderProbeAtlasTemporalAlphaDebug()
{
    float confHist = texture(probeAtlasMeta, uv).r;
    float alphaEff = clamp(temporalAlpha * clamp(confHist, 0.0, 1.0), 0.0, 1.0);
    return vec4(vec3(alphaEff), 1.0);
}

/** Renders only the ProbeAtlasTemporalAlpha view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasTemporalAlphaDebug();
}
