#version 330 core

in vec2 uv;

out vec4 outColor;

// ============================================================================
// LumOn Gather Pass
// 
// Interpolates nearby probes to compute per-pixel irradiance.
// Uses bilinear interpolation with edge-aware weighting.
// ============================================================================

// Import common utilities
@import "lumon_common.ash"

// Import SH helpers
@import "lumon_sh.fsh"

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

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Calculate screen position for this half-res pixel
    vec2 screenUV = uv;
    
    // Sample pixel depth and normal
    float pixelDepth = texture(primaryDepth, screenUV).r;
    
    // Early out for sky
    if (lumonIsSky(pixelDepth)) {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }
    
    vec3 pixelPosVS = lumonReconstructViewPos(screenUV, pixelDepth, invProjectionMatrix);
    vec3 pixelNormal = lumonDecodeNormal(texture(gBufferNormal, screenUV).xyz);
    
    // Calculate which probes surround this pixel
    vec2 screenPos = screenUV * screenSize;
    vec2 probePos = lumonScreenToProbePos(screenPos, float(probeSpacing));
    
    // Get the four surrounding probe coordinates
    ivec2 probe00 = ivec2(floor(probePos));
    ivec2 probe10 = probe00 + ivec2(1, 0);
    ivec2 probe01 = probe00 + ivec2(0, 1);
    ivec2 probe11 = probe00 + ivec2(1, 1);
    
    // Clamp to grid bounds
    ivec2 gridMax = ivec2(probeGridSize) - 1;
    probe00 = clamp(probe00, ivec2(0), gridMax);
    probe10 = clamp(probe10, ivec2(0), gridMax);
    probe01 = clamp(probe01, ivec2(0), gridMax);
    probe11 = clamp(probe11, ivec2(0), gridMax);
    
    // Bilinear interpolation weights
    vec2 frac = fract(probePos);
    
    // Sample probe data
    vec2 uv00 = lumonProbeCoordToUV(probe00, probeGridSize);
    vec2 uv10 = lumonProbeCoordToUV(probe10, probeGridSize);
    vec2 uv01 = lumonProbeCoordToUV(probe01, probeGridSize);
    vec2 uv11 = lumonProbeCoordToUV(probe11, probeGridSize);
    
    // Read SH data from all four probes
    vec4 sh0_00 = texture(radianceTexture0, uv00);
    vec4 sh1_00 = texture(radianceTexture1, uv00);
    vec4 sh0_10 = texture(radianceTexture0, uv10);
    vec4 sh1_10 = texture(radianceTexture1, uv10);
    vec4 sh0_01 = texture(radianceTexture0, uv01);
    vec4 sh1_01 = texture(radianceTexture1, uv01);
    vec4 sh0_11 = texture(radianceTexture0, uv11);
    vec4 sh1_11 = texture(radianceTexture1, uv11);
    
    // Read probe validity
    float valid00 = texture(probeAnchorPosition, uv00).w;
    float valid10 = texture(probeAnchorPosition, uv10).w;
    float valid01 = texture(probeAnchorPosition, uv01).w;
    float valid11 = texture(probeAnchorPosition, uv11).w;
    
    // Compute bilinear weights with validity masking
    float w00 = (1.0 - frac.x) * (1.0 - frac.y) * valid00;
    float w10 = frac.x * (1.0 - frac.y) * valid10;
    float w01 = (1.0 - frac.x) * frac.y * valid01;
    float w11 = frac.x * frac.y * valid11;
    
    float totalWeight = w00 + w10 + w01 + w11;
    
    // Handle case where all probes are invalid
    if (totalWeight < 0.001) {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }
    
    // Normalize weights
    float invWeight = 1.0 / totalWeight;
    w00 *= invWeight;
    w10 *= invWeight;
    w01 *= invWeight;
    w11 *= invWeight;
    
    // Interpolate SH coefficients
    vec4 sh0 = sh0_00 * w00 + sh0_10 * w10 + sh0_01 * w01 + sh0_11 * w11;
    vec4 sh1 = sh1_00 * w00 + sh1_10 * w10 + sh1_01 * w01 + sh1_11 * w11;
    
    // Unpack SH
    vec4 shR, shG, shB;
    shUnpackFromTextures(sh0, sh1, shR, shG, shB);
    
    // Evaluate SH for pixel's normal direction
    vec3 irradiance = shEvaluateDiffuseRGB(shR, shG, shB, pixelNormal);
    
    // Apply intensity and tint
    irradiance *= intensity;
    irradiance *= indirectTint;
    
    outColor = vec4(irradiance, 1.0);
}
