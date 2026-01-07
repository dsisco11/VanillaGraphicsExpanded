#version 330 core

in vec2 uv;

out vec4 outColor;

// ============================================================================
// LumOn Upsample Pass
// 
// Bilateral upsamples half-res indirect diffuse to full resolution.
// Uses edge-aware filtering based on depth and normal discontinuities.
// ============================================================================

// Import common utilities
@import "lumon_common.fsh"

// Half-resolution indirect diffuse
uniform sampler2D indirectHalf;

// G-buffer for edge detection
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Size uniforms
uniform vec2 screenSize;
uniform vec2 halfResSize;

// Z-planes
uniform float zNear;
uniform float zFar;

// Quality
uniform int denoiseEnabled;
uniform int debugMode;

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Sample center depth and normal
    float centerDepth = texture(primaryDepth, uv).r;
    
    // Early out for sky
    if (lumonIsSky(centerDepth)) {
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    
    float centerLinearDepth = lumonLinearizeDepth(centerDepth, zNear, zFar);
    vec3 centerNormal = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
    
    // Calculate UV for half-res texture
    vec2 halfResUV = uv;
    
    vec3 result;
    
    if (denoiseEnabled == 1) {
        // Bilateral upsample with 2x2 kernel
        vec2 texelSize = 1.0 / halfResSize;
        
        vec3 sum = vec3(0.0);
        float weightSum = 0.0;
        
        // Sample 2x2 neighborhood in half-res
        for (int y = 0; y <= 1; y++) {
            for (int x = 0; x <= 1; x++) {
                vec2 offset = vec2(float(x) - 0.5, float(y) - 0.5) * texelSize;
                vec2 sampleUV = halfResUV + offset;
                
                // Sample indirect
                vec3 sampleColor = texture(indirectHalf, sampleUV).rgb;
                
                // Sample full-res depth at corresponding location
                vec2 fullResOffset = offset * (screenSize / halfResSize);
                float sampleDepth = texture(primaryDepth, uv + fullResOffset).r;
                float sampleLinearDepth = lumonLinearizeDepth(sampleDepth, zNear, zFar);
                
                // Depth weight
                float depthDiff = abs(sampleLinearDepth - centerLinearDepth);
                float depthWeight = exp(-depthDiff * 10.0);
                
                // Normal weight
                vec3 sampleNormal = lumonDecodeNormal(texture(gBufferNormal, uv + fullResOffset).xyz);
                float normalWeight = max(0.0, dot(centerNormal, sampleNormal));
                normalWeight = pow(normalWeight, 4.0);
                
                // Combined weight
                float weight = depthWeight * normalWeight;
                
                sum += sampleColor * weight;
                weightSum += weight;
            }
        }
        
        result = weightSum > 0.001 ? sum / weightSum : vec3(0.0);
    } else {
        // Simple bilinear sample
        result = texture(indirectHalf, halfResUV).rgb;
    }
    
    // Debug visualization
    if (debugMode == 1) {
        // Probe grid visualization
        vec2 probePos = uv * halfResSize;
        vec2 gridPos = fract(probePos);
        float gridLine = step(0.95, gridPos.x) + step(0.95, gridPos.y);
        result = mix(result, vec3(1.0, 0.0, 0.0), gridLine * 0.5);
    } else if (debugMode == 2) {
        // Raw radiance (already showing)
    } else if (debugMode == 3) {
        // Temporal weight (would need additional data)
        result = vec3(0.5);
    } else if (debugMode == 4) {
        // Depth visualization
        result = vec3(centerLinearDepth / zFar);
    } else if (debugMode == 5) {
        // Normal visualization
        result = lumonEncodeNormal(centerNormal);
    }
    
    outColor = vec4(result, 1.0);
}
