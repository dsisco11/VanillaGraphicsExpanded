#version 330 core

// Outputs: SH9 coefficients packed across 7 render targets
layout(location = 0) out vec4 outSH0;
layout(location = 1) out vec4 outSH1;
layout(location = 2) out vec4 outSH2;
layout(location = 3) out vec4 outSH3;
layout(location = 4) out vec4 outSH4;
layout(location = 5) out vec4 outSH5;
layout(location = 6) out vec4 outSH6;

// ============================================================================
// LumOn Probe-Atlas → SH9 Projection Pass
//
// Converts each probe's 8x8 directional atlas tile into SH9 coefficients.
// Integrates each cell's angular SH moments; confidence controls reliability only.
//
// Output is per-probe SH9 (RGB) coefficients, intended for cheap gather.
// ============================================================================

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_octahedral.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/lumon_sh9.glsl"
@import "./includes/lumon_octahedral_sh9_weights.glsl"

// Shared per-frame state via UBOs.
@import "./includes/lumon_ubos.glsl"

uniform sampler2D octahedralAtlas;
uniform sampler2D probeAtlasMeta;
uniform sampler2D probeAnchorPosition; // xyz = posWS, w = validity

/** Integrates the fixed 8x8 directional cells and publishes separate sample reliability. */
void main(void)
{
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    ivec2 gridSizeI = ivec2(probeGridSize);
    probeCoord = clamp(probeCoord, ivec2(0), gridSizeI - 1);

    float probeValid = texelFetch(probeAnchorPosition, probeCoord, 0).w;
    if (probeValid < 0.5)
    {
        outSH0 = vec4(0.0);
        outSH1 = vec4(0.0);
        outSH2 = vec4(0.0);
        outSH3 = vec4(0.0);
        outSH4 = vec4(0.0);
        outSH5 = vec4(0.0);
        outSH6 = vec4(0.0);
        return;
    }

    ivec2 atlasOffset = probeCoord * LUMON_OCTAHEDRAL_SIZE;

    vec3 c0 = vec3(0.0);
    vec3 c1 = vec3(0.0);
    vec3 c2 = vec3(0.0);
    vec3 c3 = vec3(0.0);
    vec3 c4 = vec3(0.0);
    vec3 c5 = vec3(0.0);
    vec3 c6 = vec3(0.0);
    vec3 c7 = vec3(0.0);
    vec3 c8 = vec3(0.0);

    float confidenceIntegral = 0.0;

    for (int ty = 0; ty < LUMON_OCTAHEDRAL_SIZE; ty++)
    {
        for (int tx = 0; tx < LUMON_OCTAHEDRAL_SIZE; tx++)
        {
            ivec2 octTexel = ivec2(tx, ty);
            ivec2 atlasCoord = atlasOffset + octTexel;

            vec4 radianceSample = texelFetch(octahedralAtlas, atlasCoord, 0);
            vec2 meta = texelFetch(probeAtlasMeta, atlasCoord, 0).xy;

            float conf; uint flags;
            lumonDecodeMeta(meta, conf, flags);

            if (!(conf > 0.0) || any(isnan(radianceSample.rgb)) || any(isinf(radianceSample.rgb)))
                continue;

            // Fixed geometric integrals include the octahedral mapping's unequal areas
            // and cancel non-DC bands for constant radiance over the complete sphere.
            int index = (ty << 3) + tx;
            vec4 angular0 = LUMON_OCT_SH0[index];
            vec4 angular1 = LUMON_OCT_SH1[index];
            vec3 radiance = radianceSample.rgb;
            c0 += radiance * angular0.x;
            c1 += radiance * angular0.y;
            c2 += radiance * angular0.z;
            c3 += radiance * angular0.w;
            c4 += radiance * angular1.x;
            c5 += radiance * angular1.y;
            c6 += radiance * angular1.z;
            c7 += radiance * angular1.w;
            c8 += radiance * LUMON_OCT_SH2[index];
            confidenceIntegral += clamp(conf, 0.0, 1.0) * angular0.x;
        }
    }

    // Missing cells contribute neither light nor reliability. Never expand the remaining
    // samples to fill 4pi: that would invent lighting in unavailable directions.
    lumonSH9Pack(c0, c1, c2, c3, c4, c5, c6, c7, c8, outSH0, outSH1, outSH2, outSH3, outSH4, outSH5, outSH6);
    outSH6.a = clamp(confidenceIntegral / (SH9_C0 * 4.0 * LUMON_PI), 0.0, 1.0);
}
