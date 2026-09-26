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


/** Implements render probe atlas pis trace mask debug for its explicit view entrypoint. */
vec4 renderProbeAtlasPisTraceMaskDebug()
{
    ivec2 atlasSize = textureSize(probeAtlasMeta, 0);
    ivec2 atlasCoord = ivec2(clamp(uv * vec2(atlasSize), vec2(0.0), vec2(atlasSize) - vec2(1.0)));

    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;
    ivec2 octTexel = atlasCoord - probeCoord * LUMON_OCTAHEDRAL_SIZE;
    int texelIndex = octTexel.y * LUMON_OCTAHEDRAL_SIZE + octTexel.x;

    vec2 maskPacked = texelFetch(probeTraceMask, probeCoord, 0).xy;
    uvec2 maskBits = uvec2(floatBitsToUint(maskPacked.x), floatBitsToUint(maskPacked.y));

    bool validMask = (maskBits.x | maskBits.y) != 0u;
    if (!validMask)
    {
        return vec4(0.8, 0.0, 0.0, 1.0); // Red = mask invalid / not produced
    }

    bool selected;
    if (texelIndex < 32)
    {
        selected = ((maskBits.x >> uint(texelIndex)) & 1u) != 0u;
    }
    else
    {
        selected = ((maskBits.y >> uint(texelIndex - 32)) & 1u) != 0u;
    }

    // Green = traced this frame, Dark gray = preserved
    return selected ? vec4(0.1, 1.0, 0.1, 1.0) : vec4(0.12, 0.12, 0.12, 1.0);
}

/** Renders only the ProbeAtlasPisTraceMask view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasPisTraceMaskDebug();
}
