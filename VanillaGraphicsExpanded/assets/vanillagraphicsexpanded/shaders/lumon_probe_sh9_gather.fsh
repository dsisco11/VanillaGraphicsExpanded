#version 330 core

out vec4 outColor;

// ============================================================================
// LumOn Probe SH9 Gather Pass (Option B)
//
// Uses per-probe SH9 coefficients (projected from probe atlas) to evaluate
// diffuse irradiance cheaply at each pixel normal.
//
// Compared to direct atlas integration:
// - fewer texture samples (fixed small count)
// - no per-pixel hemisphere integration
// ============================================================================

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh9.glsl"
@import "./includes/lumon_worldprobe.glsl"
@import "./includes/lumon_worldprobe_gather.glsl"

// Shared per-frame state via UBOs.
@import "./includes/lumon_ubos.glsl"
@import "./includes/lumon_probe_params_ubo.glsl"

// SH9 packed textures (7 MRT attachments from projection pass)
uniform sampler2D probeSh0;
uniform sampler2D probeSh1;
uniform sampler2D probeSh2;
uniform sampler2D probeSh3;
uniform sampler2D probeSh4;
uniform sampler2D probeSh5;
uniform sampler2D probeSh6;

// Probe anchors (world-space)
uniform sampler2D probeAnchorPosition;  // xyz = posWS, w = validity
uniform sampler2D probeAnchorNormal;    // xyz = normalWS (encoded)

// G-buffer for pixel info
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Quality parameters

// ============================================================================
// World Probe Clipmap
// ============================================================================

struct ProbeData {
    vec3 posWS;
    vec3 normalWS;
    float valid;
    float depthVS;
    vec4 lastCoefficients; // c8.rgb and angularly averaged directional confidence.
};

/** Loads spatial guides and the final coefficient packet once, including directional reliability. */
ProbeData loadProbe(ivec2 probeCoord, ivec2 probeGridSizeI)
{
    ProbeData p;
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSizeI - 1);

    vec4 anchorPos = texelFetch(probeAnchorPosition, probeCoord, 0);
    p.posWS = anchorPos.xyz;
    p.valid = anchorPos.w;
    p.lastCoefficients = texelFetch(probeSh6, probeCoord, 0);
    p.lastCoefficients.a = clamp(p.lastCoefficients.a, 0.0, 1.0);

    vec4 anchorNormal = texelFetch(probeAnchorNormal, probeCoord, 0);
    p.normalWS = lumonDecodeNormal(anchorNormal.xyz);

    vec4 posVS = viewMatrix * vec4(p.posWS, 1.0);
    p.depthVS = -posVS.z;

    return p;
}

/** Combines spatial compatibility with reliability without modifying the projected radiance. */
float computeProbeWeight(
    float bilinearWeight,
    float pixelDepthVS, float probeDepthVS,
    vec3 pixelNormal, vec3 probeNormal,
    float probeValid, float directionalConfidence)
{
    if (probeValid < 0.5)
        return 0.0;

    float depthDenom = max(max(pixelDepthVS, probeDepthVS), 1.0);
    float depthDiff = abs(pixelDepthVS - probeDepthVS) / depthDenom;
    float depthWeight = exp(-depthDiff * depthDiff * 8.0);

    float normalDot = max(dot(pixelNormal, probeNormal), 0.0);
    float normalWeight = pow(normalDot, 4.0);

    return bilinearWeight * depthWeight * normalWeight * directionalConfidence;
}

/** Evaluates Lambertian SH using the coefficient packet already fetched with probe reliability. */
vec3 evaluateProbeIrradiance(ivec2 probeCoord, vec3 normalWS, vec4 lastCoefficients)
{
    vec4 t0 = texelFetch(probeSh0, probeCoord, 0);
    vec4 t1 = texelFetch(probeSh1, probeCoord, 0);
    vec4 t2 = texelFetch(probeSh2, probeCoord, 0);
    vec4 t3 = texelFetch(probeSh3, probeCoord, 0);
    vec4 t4 = texelFetch(probeSh4, probeCoord, 0);
    vec4 t5 = texelFetch(probeSh5, probeCoord, 0);
    return lumonSH9EvaluateDiffusePacked(t0, t1, t2, t3, t4, t5, lastCoefficients, normalWS);
}

/** Interpolates supported screen projections and delegates unavailable lighting to the existing fallback policy. */
void main(void)
{
    ivec2 bestFull;
    float pixelDepth;
    vec3 pixelNormalWS;
    if (!lumonSelectGuidesForHalfResCoord(ivec2(gl_FragCoord.xy), primaryDepth, gBufferNormal, ivec2(screenSize), bestFull, pixelDepth, pixelNormalWS))
    {
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    vec2 screenUV = (vec2(bestFull) + 0.5) / screenSize;

    vec3 pixelPosVS = lumonReconstructViewPos(screenUV, pixelDepth, invProjectionMatrix);
    float pixelDepthVS = -pixelPosVS.z;

    // pixelNormalWS already selected from full-res G-buffer (see helper)

    vec2 screenPos = screenUV * screenSize;
    vec2 probePos = lumonScreenToProbeAnchorPos(screenPos, float(probeSpacing));

    ivec2 probe00 = ivec2(floor(probePos));
    ivec2 probe10 = probe00 + ivec2(1, 0);
    ivec2 probe01 = probe00 + ivec2(0, 1);
    ivec2 probe11 = probe00 + ivec2(1, 1);

    vec2 frac = fract(probePos);
    float bw00 = (1.0 - frac.x) * (1.0 - frac.y);
    float bw10 = frac.x * (1.0 - frac.y);
    float bw01 = (1.0 - frac.x) * frac.y;
    float bw11 = frac.x * frac.y;

    ivec2 probeGridSizeI = ivec2(probeGridSize);

    ProbeData p00 = loadProbe(probe00, probeGridSizeI);
    ProbeData p10 = loadProbe(probe10, probeGridSizeI);
    ProbeData p01 = loadProbe(probe01, probeGridSizeI);
    ProbeData p11 = loadProbe(probe11, probeGridSizeI);

    float w00 = computeProbeWeight(bw00, pixelDepthVS, p00.depthVS, pixelNormalWS, p00.normalWS, p00.valid, p00.lastCoefficients.a);
    float w10 = computeProbeWeight(bw10, pixelDepthVS, p10.depthVS, pixelNormalWS, p10.normalWS, p10.valid, p10.lastCoefficients.a);
    float w01 = computeProbeWeight(bw01, pixelDepthVS, p01.depthVS, pixelNormalWS, p01.normalWS, p01.valid, p01.lastCoefficients.a);
    float w11 = computeProbeWeight(bw11, pixelDepthVS, p11.depthVS, pixelNormalWS, p11.normalWS, p11.valid, p11.lastCoefficients.a);

    float totalWeight = w00 + w10 + w01 + w11;
    float screenConfidence = clamp(totalWeight, 0.0, 1.0);

    if (totalWeight < 0.001)
    {
        float n00 = pow(max(dot(pixelNormalWS, p00.normalWS), 0.0), 4.0);
        float n10 = pow(max(dot(pixelNormalWS, p10.normalWS), 0.0), 4.0);
        float n01 = pow(max(dot(pixelNormalWS, p01.normalWS), 0.0), 4.0);
        float n11 = pow(max(dot(pixelNormalWS, p11.normalWS), 0.0), 4.0);

        w00 = bw00 * n00 * (p00.valid >= 0.5 ? p00.lastCoefficients.a : 0.0);
        w10 = bw10 * n10 * (p10.valid >= 0.5 ? p10.lastCoefficients.a : 0.0);
        w01 = bw01 * n01 * (p01.valid >= 0.5 ? p01.lastCoefficients.a : 0.0);
        w11 = bw11 * n11 * (p11.valid >= 0.5 ? p11.lastCoefficients.a : 0.0);
        totalWeight = w00 + w10 + w01 + w11;

        screenConfidence = clamp(totalWeight, 0.0, 1.0);
    }

    vec3 screenIrradiance = vec3(0.0);

    if (totalWeight >= 0.001)
    {
        float invW = 1.0 / totalWeight;
        w00 *= invW;
        w10 *= invW;
        w01 *= invW;
        w11 *= invW;

        vec3 irr00 = (p00.valid >= 0.5) ? evaluateProbeIrradiance(clamp(probe00, ivec2(0), probeGridSizeI - 1), pixelNormalWS, p00.lastCoefficients) : vec3(0.0);
        vec3 irr10 = (p10.valid >= 0.5) ? evaluateProbeIrradiance(clamp(probe10, ivec2(0), probeGridSizeI - 1), pixelNormalWS, p10.lastCoefficients) : vec3(0.0);
        vec3 irr01 = (p01.valid >= 0.5) ? evaluateProbeIrradiance(clamp(probe01, ivec2(0), probeGridSizeI - 1), pixelNormalWS, p01.lastCoefficients) : vec3(0.0);
        vec3 irr11 = (p11.valid >= 0.5) ? evaluateProbeIrradiance(clamp(probe11, ivec2(0), probeGridSizeI - 1), pixelNormalWS, p11.lastCoefficients) : vec3(0.0);

        screenIrradiance = irr00 * w00 + irr10 * w10 + irr01 * w01 + irr11 * w11;
        screenIrradiance = max(screenIrradiance, vec3(0.0));
    }
    else
    {
        screenConfidence = 0.0;
    }

    vec3 pixelPosWS = (invViewMatrix * vec4(pixelPosVS, 1.0)).xyz;
    LumOnWorldProbeGatherResult gatherResult = lumonResolveWorldProbeGather(
        screenIrradiance,
        screenConfidence,
        totalWeight,
        pixelPosWS,
        pixelNormalWS);

    // Suppress only a selected world replacement; do not activate the sky or screen fallback.
    if (suppressWorldProbeRadiance && gatherResult.usedWorldProbe)
    {
        gatherResult.irradiance = vec3(0.0);
    }
    vec3 blended = gatherResult.irradiance * intensity * indirectTint;
    outColor = vec4(blended, gatherResult.confidence);
}
