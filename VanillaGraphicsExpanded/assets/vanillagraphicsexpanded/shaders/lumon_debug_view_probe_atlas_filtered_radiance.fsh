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


/** Implements render probe atlas filtered radiance debug for its explicit view entrypoint. */
vec4 renderProbeAtlasFilteredRadianceDebug()
{
    vec3 rgb = texture(probeAtlasFiltered, uv).rgb;
    return vec4(rgb, 1.0);
}

/** Renders only the ProbeAtlasFilteredRadiance view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasFilteredRadianceDebug();
}
