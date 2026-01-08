#version 330 core

in vec2 uv;

// MRT outputs
layout(location = 0) out vec4 outPosition;  // posWS.xyz, valid
layout(location = 1) out vec4 outNormal;    // normalWS.xyz, reserved

// ============================================================================
// LumOn Probe Anchor Pass
// 
// Determines probe positions and normals from the G-buffer.
// Each pixel in the probe grid corresponds to a screen-space probe.
// Probes sample the center of their cell to determine anchor position.
//
// Output is in WORLD-SPACE (matching UE5 Lumen's design) for temporal stability:
// - World-space directions remain valid across camera rotations
// - Radiance stored per world-space direction can be directly blended
// - No SH rotation or coordinate transforms needed in temporal pass
//
// Validation criteria (from LumOn.02-Probe-Grid.md):
// - Depth >= 0.9999: invalid (sky, no surface to anchor)
// - Depth discontinuity: valid = 0.5 (edge, temporally unstable)
// - Normal length < 0.5: invalid (bad G-buffer data)
// - Otherwise: valid = 1.0 (good probe)
// ============================================================================

// Import common utilities
@import "lumon_common.fsh"

// G-buffer textures
uniform sampler2D primaryDepth;    // Depth buffer
uniform sampler2D gBufferNormal;   // World-space normals

// Matrices
uniform mat4 invProjectionMatrix;  // For view-space position reconstruction
uniform mat4 invViewMatrix;        // For view-space to world-space transform

// Probe grid parameters
uniform int probeSpacing;          // Pixels between probes
uniform vec2 probeGridSize;        // (probeCountX, probeCountY)
uniform vec2 screenSize;           // Full screen dimensions

// Z-planes
uniform float zNear;
uniform float zFar;

// Edge detection parameter
uniform float depthDiscontinuityThreshold;  // Recommended: 0.1

// ============================================================================
// Depth Discontinuity Detection
// ============================================================================

/**
 * Check for depth discontinuity in the neighborhood of a pixel.
 * Depth discontinuities indicate silhouette edges which are temporally unstable.
 * @param centerUV Screen UV of the probe center
 * @param centerDepth Raw depth at the center
 * @return True if significant depth discontinuity exists
 */
bool hasDepthDiscontinuity(vec2 centerUV, float centerDepth) {
    vec2 texelSize = 1.0 / screenSize;
    
    // Sample 4 neighbors
    float depthL = texture(primaryDepth, centerUV + vec2(-texelSize.x, 0.0)).r;
    float depthR = texture(primaryDepth, centerUV + vec2( texelSize.x, 0.0)).r;
    float depthU = texture(primaryDepth, centerUV + vec2(0.0,  texelSize.y)).r;
    float depthD = texture(primaryDepth, centerUV + vec2(0.0, -texelSize.y)).r;
    
    // Linearize for proper comparison (non-linear depth distorts distances)
    float linCenter = lumonLinearizeDepth(centerDepth, zNear, zFar);
    float linL = lumonLinearizeDepth(depthL, zNear, zFar);
    float linR = lumonLinearizeDepth(depthR, zNear, zFar);
    float linU = lumonLinearizeDepth(depthU, zNear, zFar);
    float linD = lumonLinearizeDepth(depthD, zNear, zFar);
    
    // Check for large depth jumps (relative threshold based on center distance)
    float threshold = linCenter * depthDiscontinuityThreshold;
    
    return abs(linCenter - linL) > threshold ||
           abs(linCenter - linR) > threshold ||
           abs(linCenter - linU) > threshold ||
           abs(linCenter - linD) > threshold;
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Get probe grid coordinates from fragment position
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    
    // Calculate the screen UV this probe samples (center of probe cell)
    vec2 screenUV = lumonProbeToScreenUV(probeCoord, float(probeSpacing), screenSize);
    
    // Check if probe is within screen bounds
    if (screenUV.x >= 1.0 || screenUV.y >= 1.0 || screenUV.x < 0.0 || screenUV.y < 0.0)
    {
        // Invalid probe - outside screen
        outPosition = vec4(0.0, 0.0, 0.0, 0.0);  // valid = 0
        outNormal = vec4(0.5, 0.5, 1.0, 0.0);    // Encoded neutral normal
        return;
    }
    
    // Sample depth at probe position
    float depth = texture(primaryDepth, screenUV).r;
    
    // ========================================================================
    // Validation Logic
    // ========================================================================
    
    float valid = 1.0;
    
    // Criterion 1: Reject sky pixels (no surface to anchor to)
    if (lumonIsSky(depth))
    {
        outPosition = vec4(0.0, 0.0, 0.0, 0.0);  // valid = 0
        outNormal = vec4(0.5, 0.5, 1.0, 0.0);    // Encoded neutral normal
        return;
    }
    
    // Criterion 2: Check for depth discontinuity (edge detection)
    // Edges are temporally unstable so mark with reduced validity
    if (hasDepthDiscontinuity(screenUV, depth))
    {
        valid = 0.5;  // Mark as edge (partial validity for reduced temporal weight)
    }
    
    // Reconstruct view-space position, then transform to world-space
    vec3 posVS = lumonReconstructViewPos(screenUV, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    
    // Sample and decode world-space normal from G-buffer (already world-space)
    vec3 normalRaw = texture(gBufferNormal, screenUV).xyz;
    vec3 normalWS = lumonDecodeNormal(normalRaw);
    
    // Criterion 3: Reject invalid normals (degenerate G-buffer data)
    if (length(normalWS) < 0.5)
    {
        outPosition = vec4(0.0, 0.0, 0.0, 0.0);  // valid = 0
        outNormal = vec4(0.5, 0.5, 1.0, 0.0);
        return;
    }
    
    // ========================================================================
    // Output (world-space for temporal stability)
    // ========================================================================
    
    // Store world-space position with validity flag
    outPosition = vec4(posWS, valid);
    
    // Store world-space normal (encoded to [0,1] range for storage)
    outNormal = vec4(lumonEncodeNormal(normalWS), 0.0);
}
