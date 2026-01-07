#version 330 core

in vec2 uv;

// MRT outputs
layout(location = 0) out vec4 outPosition;  // posVS.xyz, valid
layout(location = 1) out vec4 outNormal;    // normalVS.xyz, reserved

// ============================================================================
// LumOn Probe Anchor Pass
// 
// Determines probe positions and normals from the G-buffer.
// Each pixel in the probe grid corresponds to a screen-space probe.
// Probes sample the center of their cell to determine anchor position.
// ============================================================================

// Import common utilities
@import "lumon_common.ash"

// G-buffer textures
uniform sampler2D primaryDepth;    // Depth buffer
uniform sampler2D gBufferNormal;   // World-space normals

// Matrices
uniform mat4 invProjectionMatrix;  // For view-space position reconstruction

// Probe grid parameters
uniform int probeSpacing;          // Pixels between probes
uniform vec2 probeGridSize;        // (probeCountX, probeCountY)
uniform vec2 screenSize;           // Full screen dimensions

// Z-planes
uniform float zNear;
uniform float zFar;

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Get probe grid coordinates from fragment position
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    
    // Calculate the screen UV this probe samples
    vec2 screenUV = lumonProbeToScreenUV(probeCoord, float(probeSpacing), screenSize);
    
    // Check if probe is within screen bounds
    if (screenUV.x >= 1.0 || screenUV.y >= 1.0 || screenUV.x < 0.0 || screenUV.y < 0.0)
    {
        // Invalid probe - outside screen
        outPosition = vec4(0.0, 0.0, 0.0, 0.0);  // valid = 0
        outNormal = vec4(0.0, 0.0, 1.0, 0.0);
        return;
    }
    
    // Sample depth at probe position
    float depth = texture(primaryDepth, screenUV).r;
    
    // Check for sky (far plane)
    if (lumonIsSky(depth))
    {
        // Sky probe - mark as invalid for geometry but could be used for sky sampling
        outPosition = vec4(0.0, 0.0, 0.0, 0.0);  // valid = 0
        outNormal = vec4(0.0, 0.0, 1.0, 0.0);
        return;
    }
    
    // Reconstruct view-space position
    vec3 posVS = lumonReconstructViewPos(screenUV, depth, invProjectionMatrix);
    
    // Sample and decode world-space normal
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, screenUV).xyz);
    
    // For now, keep normal in world space (can transform to view space if needed)
    // normalVS = mat3(modelViewMatrix) * normalWS;
    vec3 normalVS = normalWS;
    
    // Output probe anchor
    outPosition = vec4(posVS, 1.0);      // valid = 1
    outNormal = vec4(normalVS, 0.0);     // reserved = 0
}
