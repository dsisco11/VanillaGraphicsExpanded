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


/** Implements lumon tonemap reinhard for its explicit view entrypoint. */
vec3 lumonTonemapReinhard(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
}

/** Implements render world probe suppressed lighting debug for its explicit view entrypoint. */
vec4 renderWorldProbeSuppressedLightingDebug()
{
    if (!worldProbeComparisonReady)
        return vec4(0.5, 0.0, 0.5, 1.0);
    return vec4(lumonTonemapReinhard(texture(worldProbeSuppressedLighting, uv).rgb), 1.0);
}

/** Renders only the WorldProbeSuppressedLighting view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderWorldProbeSuppressedLightingDebug();
}
