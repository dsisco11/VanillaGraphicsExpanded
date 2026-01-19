#version 330 core

out vec4 outColor;

// ============================================================================
// LumOn Octahedral Gather Pass
// 
// Gathers irradiance at each pixel by integrating radiance from nearby probes'
// octahedral tiles over the upper hemisphere aligned to the pixel's normal.
//
// This replaces the SH-based gather when UseOctahedralCache is enabled.
// Benefits over SH:
// - Per-direction hit distance enables leak prevention
// - Better directional fidelity (64 vs 4 coefficients)
// - World-space directions for temporal stability
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import octahedral mapping utilities
@import "./includes/lumon_octahedral.glsl"

// World-probe clipmap helpers
@import "./includes/lumon_worldprobe.glsl"

// ============================================================================
// Uniforms
// ============================================================================

// Octahedral radiance atlas: (probeCountX × 8, probeCountY × 8)
// Format: RGB = radiance, A = log-encoded hit distance
uniform sampler2D octahedralAtlas;

// Probe anchors (world-space)
uniform sampler2D probeAnchorPosition;  // xyz = posWS, w = validity
uniform sampler2D probeAnchorNormal;    // xyz = normalWS (encoded)

// G-buffer for pixel info
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Matrices
uniform mat4 invProjectionMatrix;
uniform mat4 viewMatrix;  // For depth calculation (WS probe to VS)
uniform mat4 invViewMatrix;

// Probe grid parameters
uniform int probeSpacing;
uniform vec2 probeGridSize;
uniform vec2 screenSize;
uniform vec2 halfResSize;

// Z-planes
uniform float zNear;
uniform float zFar;
// Quality parameters
uniform float intensity;
uniform vec3 indirectTint;
uniform float leakThreshold;  // Leak prevention threshold (e.g., 0.5 = 50%)

// Sampling configuration
// Use 4×4 subgrid (16 samples) for performance vs 8×8 (64 samples) for quality
uniform int sampleStride;  // 1 = full 64 samples, 2 = 16 samples

// ============================================================================
// World Probe Clipmap (Phase 18)
// ============================================================================

// ============================================================================
// Probe Data Structure
// ============================================================================

struct ProbeData {
    vec3 posWS;
    vec3 normalWS;
    float valid;
    float depthVS;  // View-space depth for weighting
};

// ============================================================================
// Load Probe Data
// ============================================================================

ProbeData loadProbe(ivec2 probeCoord, ivec2 probeGridSizeI) {
    ProbeData p;
    
    // Clamp to grid bounds
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSizeI - 1);
    
    // Load anchor position and validity
    vec4 anchorPos = texelFetch(probeAnchorPosition, probeCoord, 0);
    p.posWS = anchorPos.xyz;
    p.valid = anchorPos.w;
    
    // Load anchor normal
    vec4 anchorNormal = texelFetch(probeAnchorNormal, probeCoord, 0);
    p.normalWS = lumonDecodeNormal(anchorNormal.xyz);
    
    // Compute view-space depth for weighting
    vec4 posVS = viewMatrix * vec4(p.posWS, 1.0);
    p.depthVS = -posVS.z;  // Positive distance from camera
    
    return p;
}

// ============================================================================
// Hemisphere Integration
// ============================================================================

/**
 * Integrate radiance over the upper hemisphere of a single probe's octahedral tile.
 * Uses cosine weighting for Lambertian diffuse.
 *
 * @param probeCoord       Probe grid coordinates
 * @param normalWS         Pixel's world-space normal (hemisphere axis)
 * @param pixelDepthVS     Pixel's view-space depth for leak prevention
 * @param probeGridSizeI   Probe grid dimensions
 * @param avgHitDist       Output: average hit distance for this probe
 * @return Integrated irradiance
 */
vec3 integrateHemisphere(ivec2 probeCoord, vec3 normalWS, float pixelDepthVS,
                         ivec2 probeGridSizeI, out float avgHitDist) {
    vec3 irradiance = vec3(0.0);
    float totalWeight = 0.0;
    float hitDistSum = 0.0;
    int hitDistCount = 0;
    
    // Calculate atlas offset for this probe's 8×8 tile
    ivec2 atlasOffset = probeCoord * LUMON_OCTAHEDRAL_SIZE;
    
    // Determine stride (1 = 64 samples, 2 = 16 samples)
    int stride = max(1, sampleStride);
    
    // Sample octahedral texels
    for (int y = 0; y < LUMON_OCTAHEDRAL_SIZE; y += stride) {
        for (int x = 0; x < LUMON_OCTAHEDRAL_SIZE; x += stride) {
            // Convert texel to world-space direction
            vec2 octUV = lumonTexelCoordToOctahedralUV(ivec2(x, y));
            vec3 dir = lumonOctahedralUVToDirection(octUV);
            
            // Cosine weight - skip backfacing directions (lower hemisphere)
            float cosWeight = dot(dir, normalWS);
            if (cosWeight <= 0.0) continue;
            
            // Sample radiance + hit distance from atlas
            ivec2 atlasCoord = atlasOffset + ivec2(x, y);
            vec4 sampleData = texelFetch(octahedralAtlas, atlasCoord, 0);
            vec3 radiance = sampleData.rgb;
            float hitDist = lumonDecodeHitDistance(sampleData.a);
            
            // Accumulate with cosine weight
            irradiance += radiance * cosWeight;
            totalWeight += cosWeight;
            
            // Track hit distances for probe weighting
            hitDistSum += hitDist;
            hitDistCount++;
        }
    }
    
    // Compute average hit distance
    avgHitDist = (hitDistCount > 0) ? hitDistSum / float(hitDistCount) : pixelDepthVS;
    
    // Normalize irradiance
    return (totalWeight > 0.001) ? irradiance / totalWeight : vec3(0.0);
}

// ============================================================================
// Probe Weight Calculation
// ============================================================================

/**
 * Compute edge-aware probe weight considering geometry and hit distance.
 */
float computeProbeWeight(float bilinearWeight,
                         float pixelDepthVS, float probeDepthVS,
                         vec3 pixelNormal, vec3 probeNormal,
                         float avgHitDist, float probeValid) {
    if (probeValid < 0.5) {
        return 0.0;
    }
    
    // Depth similarity - penalize probes at very different depths.
    // IMPORTANT: Near the camera, pixelDepthVS can be very small; normalizing solely
    // by pixelDepthVS makes depthDiff explode and collapses all weights to ~0,
    // producing a hard black cutoff in the indirect buffer.
    float depthDenom = max(max(pixelDepthVS, probeDepthVS), 1.0);
    float depthDiff = abs(pixelDepthVS - probeDepthVS) / depthDenom;
    float depthWeight = exp(-depthDiff * depthDiff * 8.0);
    
    // Normal similarity - penalize probes with different surface orientation
    float normalDot = max(dot(pixelNormal, probeNormal), 0.0);
    float normalWeight = pow(normalDot, 4.0);
    
    // Distance-based weight: prefer probes with similar scene distance.
    // Same stability concern as depthDiff: clamp denominator to avoid near-plane blowups.
    float distRatio = avgHitDist / depthDenom;
    float distWeight = exp(-abs(distRatio - 1.0) * 2.0);
    
    // Combine all weights
    return bilinearWeight * depthWeight * normalWeight * distWeight;
}

// ============================================================================
// Main
// ============================================================================

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
    
    // Reconstruct pixel position and get normal
    vec3 pixelPosVS = lumonReconstructViewPos(screenUV, pixelDepth, invProjectionMatrix);
    float pixelDepthVS = -pixelPosVS.z;  // Positive depth
    
    // pixelNormalWS already selected from full-res G-buffer (see helper)
    
    // Calculate which probes surround this pixel
    vec2 screenPos = screenUV * screenSize;
    vec2 probePos = lumonScreenToProbePos(screenPos, float(probeSpacing));
    
    // Get the four surrounding probe coordinates
    ivec2 probe00 = ivec2(floor(probePos));
    ivec2 probe10 = probe00 + ivec2(1, 0);
    ivec2 probe01 = probe00 + ivec2(0, 1);
    ivec2 probe11 = probe00 + ivec2(1, 1);
    
    // Bilinear interpolation weights
    vec2 frac = fract(probePos);
    float bw00 = (1.0 - frac.x) * (1.0 - frac.y);
    float bw10 = frac.x * (1.0 - frac.y);
    float bw01 = (1.0 - frac.x) * frac.y;
    float bw11 = frac.x * frac.y;
    
    ivec2 probeGridSizeI = ivec2(probeGridSize);
    
    // Load probe data
    ProbeData p00 = loadProbe(probe00, probeGridSizeI);
    ProbeData p10 = loadProbe(probe10, probeGridSizeI);
    ProbeData p01 = loadProbe(probe01, probeGridSizeI);
    ProbeData p11 = loadProbe(probe11, probeGridSizeI);
    
    // Integrate hemisphere for each valid probe
    float avgDist00, avgDist10, avgDist01, avgDist11;
    
    vec3 irr00 = (p00.valid >= 0.5) 
        ? integrateHemisphere(probe00, pixelNormalWS, pixelDepthVS, probeGridSizeI, avgDist00)
        : vec3(0.0);
    vec3 irr10 = (p10.valid >= 0.5)
        ? integrateHemisphere(probe10, pixelNormalWS, pixelDepthVS, probeGridSizeI, avgDist10)
        : vec3(0.0);
    vec3 irr01 = (p01.valid >= 0.5)
        ? integrateHemisphere(probe01, pixelNormalWS, pixelDepthVS, probeGridSizeI, avgDist01)
        : vec3(0.0);
    vec3 irr11 = (p11.valid >= 0.5)
        ? integrateHemisphere(probe11, pixelNormalWS, pixelDepthVS, probeGridSizeI, avgDist11)
        : vec3(0.0);
    
    // Initialize avgDist for invalid probes
    if (p00.valid < 0.5) avgDist00 = 999.0;
    if (p10.valid < 0.5) avgDist10 = 999.0;
    if (p01.valid < 0.5) avgDist01 = 999.0;
    if (p11.valid < 0.5) avgDist11 = 999.0;
    
    // Compute edge-aware weights
    float w00 = computeProbeWeight(bw00, pixelDepthVS, p00.depthVS, pixelNormalWS, p00.normalWS, avgDist00, p00.valid);
    float w10 = computeProbeWeight(bw10, pixelDepthVS, p10.depthVS, pixelNormalWS, p10.normalWS, avgDist10, p10.valid);
    float w01 = computeProbeWeight(bw01, pixelDepthVS, p01.depthVS, pixelNormalWS, p01.normalWS, avgDist01, p01.valid);
    float w11 = computeProbeWeight(bw11, pixelDepthVS, p11.depthVS, pixelNormalWS, p11.normalWS, avgDist11, p11.valid);
    
    float totalWeight = w00 + w10 + w01 + w11;
    
    // If edge-aware weighting collapses (common very near the camera), fall back to
    // bilinear weighting with normal similarity + validity (dropping depth/dist penalties)
    // so we avoid a hard black cutoff without reintroducing obvious normal leaks.
    if (totalWeight < 0.001) {
        float n00 = pow(max(dot(pixelNormalWS, p00.normalWS), 0.0), 4.0);
        float n10 = pow(max(dot(pixelNormalWS, p10.normalWS), 0.0), 4.0);
        float n01 = pow(max(dot(pixelNormalWS, p01.normalWS), 0.0), 4.0);
        float n11 = pow(max(dot(pixelNormalWS, p11.normalWS), 0.0), 4.0);

        w00 = bw00 * n00 * (p00.valid >= 0.5 ? 1.0 : 0.0);
        w10 = bw10 * n10 * (p10.valid >= 0.5 ? 1.0 : 0.0);
        w01 = bw01 * n01 * (p01.valid >= 0.5 ? 1.0 : 0.0);
        w11 = bw11 * n11 * (p11.valid >= 0.5 ? 1.0 : 0.0);
        totalWeight = w00 + w10 + w01 + w11;

        if (totalWeight < 0.001) {
            // alpha encodes weight diagnostics; keep it 0 for totally invalid.
            outColor = vec4(0.0, 0.0, 0.0, 0.0);
            return;
        }
    }
    
    // Screen confidence (raw weight sum, clamped)
    float screenConfidence = clamp(totalWeight, 0.0, 1.0);

    // Normalize weights
    float invWeight = 1.0 / totalWeight;
    w00 *= invWeight;
    w10 *= invWeight;
    w01 *= invWeight;
    w11 *= invWeight;

    // Blend irradiance from all probes (screen-space GI)
    vec3 screenIrradiance = irr00 * w00 + irr10 * w10 + irr01 * w01 + irr11 * w11;
    screenIrradiance = max(screenIrradiance, vec3(0.0));

    // World-probe sample (WORLD space)
    vec3 worldIrradiance = vec3(0.0);
    float worldConfidence = 0.0;

#if VGE_LUMON_WORLDPROBE_ENABLED
    vec3 pixelPosWS = (invViewMatrix * vec4(pixelPosVS, 1.0)).xyz;
    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(pixelPosWS, pixelNormalWS);

    worldIrradiance = wp.irradiance;
    worldConfidence = wp.confidence;
#endif

    float screenW = screenConfidence;
    float worldW = worldConfidence * (1.0 - screenW);
    float sumW = screenW + worldW;

    vec3 blended = (sumW > 1e-3)
        ? (screenIrradiance * screenW + worldIrradiance * worldW) / sumW
        : vec3(0.0);

    float outConfidence = clamp(sumW, 0.0, 1.0);

    blended *= intensity;
    blended *= indirectTint;

    outColor = vec4(blended, outConfidence);
}
