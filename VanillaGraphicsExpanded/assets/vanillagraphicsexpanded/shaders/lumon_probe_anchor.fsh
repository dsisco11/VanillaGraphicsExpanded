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
// Utility Functions
// ============================================================================

// Linearize depth from depth buffer
float linearizeDepth(float depth) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

// Reconstruct view-space position from UV and depth
vec3 reconstructViewPos(vec2 texCoord, float depth) {
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = invProjectionMatrix * ndc;
    return viewPos.xyz / viewPos.w;
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Get probe grid coordinates from fragment position
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    
    // Calculate the screen-space position this probe samples
    // Probes sample at the center of their cell
    vec2 screenPos = (vec2(probeCoord) + 0.5) * float(probeSpacing);
    
    // Convert to UV coordinates
    vec2 screenUV = screenPos / screenSize;
    
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
    if (depth >= 0.9999)
    {
        // Sky probe - mark as invalid for geometry but could be used for sky sampling
        outPosition = vec4(0.0, 0.0, 0.0, 0.0);  // valid = 0
        outNormal = vec4(0.0, 0.0, 1.0, 0.0);
        return;
    }
    
    // Reconstruct view-space position
    vec3 posVS = reconstructViewPos(screenUV, depth);
    
    // Sample world-space normal and transform to view-space
    vec3 normalWS = texture(gBufferNormal, screenUV).xyz * 2.0 - 1.0;
    normalWS = normalize(normalWS);
    
    // For now, keep normal in world space (can transform to view space if needed)
    // normalVS = mat3(modelViewMatrix) * normalWS;
    vec3 normalVS = normalWS;
    
    // Output probe anchor
    outPosition = vec4(posVS, 1.0);      // valid = 1
    outNormal = vec4(normalVS, 0.0);     // reserved = 0
}
