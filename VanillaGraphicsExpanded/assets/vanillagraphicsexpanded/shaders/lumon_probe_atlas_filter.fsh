#version 330 core

layout(location = 0) out vec4 outRadiance;  // RGB = filtered radiance, A = filtered hit distance (log-encoded)
layout(location = 1) out vec2 outMeta;      // R = filtered confidence, G = uintBitsToFloat(flags)

// ============================================================================
// LumOn Probe-Atlas Filter Pass (Probe-space denoise)
//
// Edge-stopped filter performed within each probe's 8x8 octahedral tile.
// Intended to smooth per-texel noise after temporal accumulation while avoiding:
// - bleeding across hit-distance discontinuities
// - smearing across hit/miss classification boundaries
// - writing anything for invalid probes
//
// Output is a filtered atlas that can be used as gather input.
// ============================================================================

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_octahedral.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"

// Input stabilized atlas (typically the temporal output for this frame)
uniform sampler2D octahedralAtlas;

// Input meta for stabilized atlas (confidence + flags)
uniform sampler2D probeAtlasMeta;

// Probe anchors for validity check
uniform sampler2D probeAnchorPosition;  // xyz = posWS, w = validity

// Probe grid parameters
uniform vec2 probeGridSize;

// Filter parameters
uniform int filterRadius;         // e.g. 1 for 3x3
uniform float hitDistanceSigma;   // decoded distance sigma for edge stopping

float gaussian(float x, float sigma)
{
    // Avoid div by zero
    sigma = max(sigma, 1e-3);
    return exp(-(x * x) / (2.0 * sigma * sigma));
}

void main(void)
{
    ivec2 atlasCoord = ivec2(gl_FragCoord.xy);
    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;
    ivec2 octTexel = atlasCoord % LUMON_OCTAHEDRAL_SIZE;

    ivec2 probeGridSizeI = ivec2(probeGridSize);
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSizeI - 1);

    float probeValid = texelFetch(probeAnchorPosition, probeCoord, 0).w;
    if (probeValid < 0.5)
    {
        outRadiance = vec4(0.0);
        outMeta = lumonEncodeMeta(0.0, 0u);
        return;
    }

    vec4 center = texelFetch(octahedralAtlas, atlasCoord, 0);
    vec2 metaCenter = texelFetch(probeAtlasMeta, atlasCoord, 0).xy;

    float confCenter; uint flagsCenter;
    lumonDecodeMeta(metaCenter, confCenter, flagsCenter);

    float centerHitDist = lumonDecodeHitDistance(center.a);
    bool centerHit = (flagsCenter & LUMON_META_HIT) != 0u;

    ivec2 atlasOffset = probeCoord * LUMON_OCTAHEDRAL_SIZE;

    vec4 accum = vec4(0.0);
    float totalW = 0.0;

    float confAccum = 0.0;
    float confDenom = 0.0;

    // Spatial sigma tuned for 3x3; for larger radii this still behaves reasonably.
    const float spatialSigma = 1.0;

    for (int dy = -8; dy <= 8; dy++)
    {
        if (dy < -filterRadius || dy > filterRadius) continue;

        for (int dx = -8; dx <= 8; dx++)
        {
            if (dx < -filterRadius || dx > filterRadius) continue;

            ivec2 neighborTexel = clamp(octTexel + ivec2(dx, dy), ivec2(0), ivec2(LUMON_OCTAHEDRAL_SIZE - 1));
            ivec2 neighborAtlas = atlasOffset + neighborTexel;

            vec4 sample = texelFetch(octahedralAtlas, neighborAtlas, 0);
            vec2 metaSample = texelFetch(probeAtlasMeta, neighborAtlas, 0).xy;

            float confS; uint flagsS;
            lumonDecodeMeta(metaSample, confS, flagsS);

            // Treat non-positive confidence as unusable.
            if (confS <= 0.0) continue;

            bool sampleHit = (flagsS & LUMON_META_HIT) != 0u;
            if (sampleHit != centerHit) continue;

            float sampleHitDist = lumonDecodeHitDistance(sample.a);

            float spatialW = gaussian(length(vec2(dx, dy)), spatialSigma);
            float hitW = gaussian(abs(sampleHitDist - centerHitDist), hitDistanceSigma);

            float wBase = spatialW * hitW;
            float w = wBase * confS;

            accum += sample * w;
            totalW += w;

            confAccum += confS * wBase;
            confDenom += wBase;
        }
    }

    if (totalW < 1e-6)
    {
        outRadiance = center;
        outMeta = lumonEncodeMeta(confCenter, flagsCenter);
        return;
    }

    vec4 filtered = accum / totalW;
    float outConf = (confDenom > 1e-6) ? clamp(confAccum / confDenom, 0.0, 1.0) : confCenter;

    outRadiance = filtered;
    outMeta = lumonEncodeMeta(outConf, flagsCenter);
}
