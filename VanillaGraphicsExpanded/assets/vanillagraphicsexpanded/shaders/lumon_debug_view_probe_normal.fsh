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


/** Implements render probe normal debug for its explicit view entrypoint. */
vec4 renderProbeNormalDebug(vec2 screenPos)
{
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);

    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;

    if (valid < 0.1)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid
    }

    // Decode normal from [0,1] to [-1,1], then re-encode for visualization
    vec3 probeNormalEncoded = texelFetch(probeAnchorNormal, probeCoord, 0).xyz;
    vec3 probeNormalDecoded = lumonDecodeNormal(probeNormalEncoded);
    // Display as color: remap [-1,1] to [0,1] so all directions are visible
    return vec4(probeNormalDecoded * 0.5 + 0.5, 1.0);
}

/** Renders only the ProbeNormal view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeNormalDebug(screenPos);
}
