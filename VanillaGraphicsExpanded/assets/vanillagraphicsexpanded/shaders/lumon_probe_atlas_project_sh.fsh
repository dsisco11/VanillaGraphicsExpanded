#version 330 core

// MRT outputs for SH radiance cache (packed)
layout(location = 0) out vec4 outRadiance0;
layout(location = 1) out vec4 outRadiance1;

// ============================================================================
// LumOn Probe-Atlas → SH Projection Pass (Option B)
//
// Projects the stabilized (temporal+filtered) screen-probe atlas into SH L1
// coefficients per probe, so downstream gather can be cheap (SH evaluate).
//
// Notes:
// - SH basis is evaluated in view-space directions to match the existing SH gather.
// - Confidence from probeAtlasMeta is used as a per-texel weight.
// ============================================================================

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_octahedral.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"

// Phase 23: shared per-frame state via UBOs.
@import "./includes/lumon_ubos.glsl"

uniform sampler2D octahedralAtlas;   // RGBA16F, tiled 8x8 per probe
uniform sampler2D probeAtlasMeta;    // RG32F, confidence + flags
uniform sampler2D probeAnchorPosition; // xyz = posWS, w = valid

void main(void)
{
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    ivec2 gridMax = ivec2(probeGridSize) - 1;
    probeCoord = clamp(probeCoord, ivec2(0), gridMax);

    float probeValid = texelFetch(probeAnchorPosition, probeCoord, 0).w;
    if (probeValid < 0.5)
    {
        outRadiance0 = vec4(0.0);
        outRadiance1 = vec4(0.0);
        return;
    }

    ivec2 atlasOffset = probeCoord * LUMON_OCTAHEDRAL_SIZE;

    vec4 shR = vec4(0.0);
    vec4 shG = vec4(0.0);
    vec4 shB = vec4(0.0);

    float weightSum = 0.0;

    for (int i = 0; i < LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE; i++)
    {
        ivec2 octTexel = ivec2(i % LUMON_OCTAHEDRAL_SIZE, i / LUMON_OCTAHEDRAL_SIZE);
        ivec2 atlasCoord = atlasOffset + octTexel;

        vec4 sample = texelFetch(octahedralAtlas, atlasCoord, 0);
        vec2 meta = texelFetch(probeAtlasMeta, atlasCoord, 0).xy;

        float conf; uint flags;
        lumonDecodeMeta(meta, conf, flags);

        // Skip unusable samples
        if (conf <= 0.0) continue;

        // Octahedral direction is defined in world-space; convert to view-space for SH.
        vec2 octUV = lumonTexelCoordToOctahedralUV(octTexel);
        vec3 dirWS = lumonOctahedralUVToDirection(octUV);
        vec3 dirVS = normalize(mat3(viewMatrix) * dirWS);

        shProjectRGB(shR, shG, shB, dirVS, sample.rgb, conf);
        weightSum += conf;
    }

    if (weightSum > 0.0)
    {
        float invW = 1.0 / weightSum;
        shR *= invW;
        shG *= invW;
        shB *= invW;
    }

    shClampNegative(shR, shG, shB);
    shPackToTextures(shR, shG, shB, outRadiance0, outRadiance1);
}
