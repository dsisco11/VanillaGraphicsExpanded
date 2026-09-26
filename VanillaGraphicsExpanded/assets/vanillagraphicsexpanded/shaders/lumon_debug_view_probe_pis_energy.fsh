#version 330 core

vec2 uv;
out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/lumon_octahedral.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/heatmap.glsl"

/** Implements render probe pis energy debug for its explicit view entrypoint. */
vec4 renderProbePisEnergyDebug()
{
    ivec2 atlasSize = textureSize(probeAtlasMeta, 0);
    ivec2 atlasCoord = ivec2(clamp(uv * vec2(atlasSize), vec2(0.0), vec2(atlasSize) - vec2(1.0)));
    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;

    float e = texelFetch(probePisEnergy, probeCoord, 0).r;

    // Log-ish compression to keep ranges visible.
    float t = clamp(log2(1.0 + e) / 8.0, 0.0, 1.0);
    return vec4(heatmap(t), 1.0);
}

/** Renders only the ProbePisEnergy view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbePisEnergyDebug();
}
