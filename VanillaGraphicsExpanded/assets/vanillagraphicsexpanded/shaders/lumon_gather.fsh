#version 330 core

out vec4 outColor;

// ============================================================================
// LumOn Gather Pass (SPG-007)
// 
// Interpolates nearby probes to compute per-pixel irradiance.
// Uses bilinear interpolation with edge-aware weighting.
//
// Edge-aware weighting reduces light leaking across depth/normal discontinuities
// by adjusting probe weights based on similarity to the pixel's geometry.
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import SH helpers
@import "./includes/lumon_sh.glsl"

// Radiance textures (from temporal pass)
uniform sampler2D radianceTexture0;
uniform sampler2D radianceTexture1;

// Probe anchors
uniform sampler2D probeAnchorPosition;
uniform sampler2D probeAnchorNormal;

// G-buffer for pixel info
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Matrices
uniform mat4 invProjectionMatrix;
uniform mat4 viewMatrix;  // For WS to VS normal transform (SH stored in VS directions)

// Probe grid parameters
uniform int probeSpacing;
uniform vec2 probeGridSize;
uniform vec2 screenSize;
uniform vec2 halfResSize;

// Z-planes
uniform float zNear;
uniform float zFar;

// Quality parameters
uniform float depthDiscontinuityThreshold;
uniform float intensity;
uniform vec3 indirectTint;

// Edge-aware weighting parameters (from spec Section 2.3)
uniform float depthSigma;   // Controls depth similarity falloff (default: 0.5)
uniform float normalSigma;  // Controls normal similarity power (default: 8.0)

// ============================================================================
// Probe Data Structure
// ============================================================================

struct ProbeData {
    vec4 shR, shG, shB;   // SH coefficients for RGB
    vec3 posVS;           // View-space position
    vec3 normalVS;        // View-space normal
    float valid;          // Validity flag (1.0 = solid, 0.5 = edge, 0.0 = invalid)
    float depth;          // Linear depth (positive)
};

// ============================================================================
// Load Probe Data
// ============================================================================

ProbeData loadProbeData(ivec2 probeCoord) {
    ProbeData p;
    
    // Clamp to grid bounds
    ivec2 gridMax = ivec2(probeGridSize) - 1;
    probeCoord = clamp(probeCoord, ivec2(0), gridMax);
    
    // Load anchor position and validity.
    // Anchor pass stores WORLD-space position for temporal stability.
    vec4 anchorPos = texelFetch(probeAnchorPosition, probeCoord, 0);
    vec3 posWS = anchorPos.xyz;
    p.valid = anchorPos.w;

    // Convert to view-space for depth/SH evaluation.
    // In view-space, forward is -Z, so linear depth is -posVS.z.
    p.posVS = (viewMatrix * vec4(posWS, 1.0)).xyz;
    p.depth = -p.posVS.z;
    
    // Load anchor normal.
    // Anchor pass stores WORLD-space normal (encoded). Convert to view-space.
    vec3 normalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    p.normalVS = normalize(mat3(viewMatrix) * normalWS);
    
    // Load SH radiance
    vec4 sh0 = texelFetch(radianceTexture0, probeCoord, 0);
    vec4 sh1 = texelFetch(radianceTexture1, probeCoord, 0);
    shUnpackFromTextures(sh0, sh1, p.shR, p.shG, p.shB);
    
    return p;
}

// ============================================================================
// Edge-Aware Weight Calculation (Spec Section 2.3)
// ============================================================================

/**
 * Compute edge-aware weight for a probe based on geometric similarity.
 * Reduces weight for probes with significantly different depth or normal
 * from the pixel being shaded, preventing light leaking across edges.
 *
 * @param bilinearWeight Base bilinear interpolation weight
 * @param pixelDepth     Pixel's linear depth
 * @param probeDepth     Probe's linear depth
 * @param pixelNormal    Pixel's normal (view-space)
 * @param probeNormal    Probe's normal (view-space)
 * @param probeValid     Probe validity (0.0-1.0)
 * @return Adjusted weight
 */
float computeEdgeAwareWeight(float bilinearWeight,
                             float pixelDepth, float probeDepth,
                             vec3 pixelNormal, vec3 probeNormal,
                             float probeValid) {
    // Invalid probes get zero weight
    if (probeValid < 0.5) {
        return 0.0;
    }
    
    // Depth similarity - Gaussian falloff based on relative depth difference
    float depthDiff = abs(pixelDepth - probeDepth) / max(pixelDepth, 0.01);
    float depthWeight = exp(-depthDiff * depthDiff / (2.0 * depthSigma * depthSigma));
    
    // Normal similarity - power falloff based on dot product
    float normalDot = max(dot(pixelNormal, probeNormal), 0.0);
    float normalWeight = pow(normalDot, normalSigma);
    
    // Reduce weight for edge probes (validity < 1.0)
    float edgeFactor = probeValid;
    
    return bilinearWeight * depthWeight * normalWeight * edgeFactor;
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // We're rendering at half-res, but sample full-res depth/normal guides.
    // Use a robust 2x2 selection to avoid silhouette artifacts.
    ivec2 bestFull;
    float pixelDepthRaw;
    vec3 pixelNormalWS;
    if (!lumonSelectGuidesForHalfResCoord(ivec2(gl_FragCoord.xy), primaryDepth, gBufferNormal, ivec2(screenSize), bestFull, pixelDepthRaw, pixelNormalWS))
    {
        // Treat sky as low-confidence so it never becomes "trusted black".
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    vec2 screenUV = (vec2(bestFull) + 0.5) / screenSize;
    
    // Reconstruct pixel position and compute linear depth
    vec3 pixelPosVS = lumonReconstructViewPos(screenUV, pixelDepthRaw, invProjectionMatrix);
    float pixelDepth = -pixelPosVS.z;  // Positive linear depth
    
    // Get pixel normal in world-space, then transform to view-space
    vec3 pixelNormalVS = normalize(mat3(viewMatrix) * pixelNormalWS);
    
    // Calculate which probes surround this pixel
    vec2 screenPos = screenUV * screenSize;
    vec2 probePos = lumonScreenToProbePos(screenPos, float(probeSpacing));
    
    // Get the four surrounding probe coordinates
    ivec2 probe00 = ivec2(floor(probePos));
    ivec2 probe10 = probe00 + ivec2(1, 0);
    ivec2 probe01 = probe00 + ivec2(0, 1);
    ivec2 probe11 = probe00 + ivec2(1, 1);
    
    // Calculate bilinear base weights
    vec2 frac = fract(probePos);
    float bw00 = (1.0 - frac.x) * (1.0 - frac.y);
    float bw10 = frac.x * (1.0 - frac.y);
    float bw01 = (1.0 - frac.x) * frac.y;
    float bw11 = frac.x * frac.y;
    
    // Load probe data (position, normal, validity, SH)
    ProbeData p00 = loadProbeData(probe00);
    ProbeData p10 = loadProbeData(probe10);
    ProbeData p01 = loadProbeData(probe01);
    ProbeData p11 = loadProbeData(probe11);
    
    // Compute edge-aware weights (spec Section 2.3)
    float w00 = computeEdgeAwareWeight(bw00, pixelDepth, p00.depth, pixelNormalVS, p00.normalVS, p00.valid);
    float w10 = computeEdgeAwareWeight(bw10, pixelDepth, p10.depth, pixelNormalVS, p10.normalVS, p10.valid);
    float w01 = computeEdgeAwareWeight(bw01, pixelDepth, p01.depth, pixelNormalVS, p01.normalVS, p01.valid);
    float w11 = computeEdgeAwareWeight(bw11, pixelDepth, p11.depth, pixelNormalVS, p11.normalVS, p11.valid);
    
    float totalWeight = w00 + w10 + w01 + w11;
    
    // Handle case where all probes are invalid
    if (totalWeight < 0.001) {
        // Alpha is used as a confidence/quality measure for later passes.
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    // Preserve the raw weight sum as a confidence/quality metric.
    // Since the bilinear weights sum to 1.0 and all modifiers are <= 1.0,
    // totalWeight is expected to be in [0, 1] for valid pixels.
    float confidence = clamp(totalWeight, 0.0, 1.0);
    
    // Normalize weights
    float invWeight = 1.0 / totalWeight;
    w00 *= invWeight;
    w10 *= invWeight;
    w01 *= invWeight;
    w11 *= invWeight;
    
    // Interpolate SH coefficients with edge-aware weights
    vec4 shR = p00.shR * w00 + p10.shR * w10 + p01.shR * w01 + p11.shR * w11;
    vec4 shG = p00.shG * w00 + p10.shG * w10 + p01.shG * w01 + p11.shG * w11;
    vec4 shB = p00.shB * w00 + p10.shB * w10 + p01.shB * w01 + p11.shB * w11;
    
    // Evaluate SH for pixel's normal direction (cosine-weighted diffuse)
    vec3 irradiance = shEvaluateDiffuseRGB(shR, shG, shB, pixelNormalVS);
    
    // Clamp negative values (can occur due to SH ringing)
    irradiance = max(irradiance, vec3(0.0));
    
    // Apply intensity and tint
    irradiance *= intensity;
    irradiance *= indirectTint;
    
    outColor = vec4(irradiance, confidence);
}
