#version 330 core

out vec4 outColor;

// ============================================================================
// LumOn Upsample Pass (SPG-008)
// 
// Bilateral upsamples half-res indirect diffuse to full resolution.
// Uses edge-aware filtering based on depth and normal discontinuities.
//
// The bilateral filter preserves edges by reducing contribution from
// samples that have significantly different depth or normal from the
// center pixel.
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

// Quality parameters (from spec Section 3.1)
uniform int denoiseEnabled;
uniform float upsampleDepthSigma;   // e.g., 0.1
uniform float upsampleNormalSigma;  // e.g., 16.0
uniform float upsampleSpatialSigma; // e.g., 1.0

// ============================================================================
// Bilateral Upsample
// ============================================================================

/**
 * Bilateral upsample from half-res to full-res with edge-awareness.
 * Uses 2x2 kernel in half-res space with depth/normal guided weights.
 */
vec3 bilateralUpsample(vec2 fullResUV, float centerDepth, vec3 centerNormal) {
    // Map to half-res coordinates
    vec2 halfResCoord = fullResUV * halfResSize - 0.5;
    ivec2 baseCoord = ivec2(floor(halfResCoord));
    vec2 fracCoord = fract(halfResCoord);
    
    vec3 result = vec3(0.0);
    float totalWeight = 0.0;
    
    // Sample 2x2 neighborhood in half-res
    for (int dy = 0; dy <= 1; dy++) {
        for (int dx = 0; dx <= 1; dx++) {
            ivec2 sampleCoord = baseCoord + ivec2(dx, dy);
            
            // Clamp to bounds
            sampleCoord = clamp(sampleCoord, ivec2(0), ivec2(halfResSize) - 1);
            
            // Map back to full-res for guide sampling
            vec2 sampleFullResUV = (vec2(sampleCoord) * 2.0 + 1.0) / screenSize;
            
            // Sample guides at corresponding full-res location
            float sampleDepthRaw = texture(primaryDepth, sampleFullResUV).r;
            float sampleDepth = lumonLinearizeDepth(sampleDepthRaw, zNear, zFar);
            vec3 sampleNormal = lumonDecodeNormal(texture(gBufferNormal, sampleFullResUV).xyz);
            
            // Bilinear weight
            float bx = (dx == 0) ? (1.0 - fracCoord.x) : fracCoord.x;
            float by = (dy == 0) ? (1.0 - fracCoord.y) : fracCoord.y;
            float bilinearWeight = bx * by;
            
            // Depth weight - Gaussian falloff based on relative depth difference
            float depthDiff = abs(centerDepth - sampleDepth) / max(centerDepth, 0.01);
            float depthWeight = exp(-depthDiff * depthDiff / 
                                    (2.0 * upsampleDepthSigma * upsampleDepthSigma));
            
            // Normal weight - power falloff based on dot product
            float normalDot = max(dot(centerNormal, sampleNormal), 0.0);
            float normalWeight = pow(normalDot, upsampleNormalSigma);
            
            float weight = bilinearWeight * depthWeight * normalWeight;
            
            // Sample half-res indirect
            vec3 indirect = texelFetch(indirectHalf, sampleCoord, 0).rgb;
            
            result += indirect * weight;
            totalWeight += weight;
        }
    }
    
    if (totalWeight > 0.001) {
        result /= totalWeight;
    }
    
    return result;
}

// ============================================================================
// Optional: Edge-Aware Spatial Denoise
// ============================================================================

/**
 * Additional 3x3 spatial denoise pass using bilateral filtering.
 * Applied after upsample for smoother results in noisy areas.
 */
vec3 spatialDenoise(vec2 fullResUV, vec3 centerColor, float centerDepth, vec3 centerNormal) {
    vec3 result = centerColor;
    float totalWeight = 1.0;
    
    // 3x3 kernel
    vec2 texelSize = 1.0 / screenSize;
    
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            if (dx == 0 && dy == 0) continue;
            
            vec2 sampleUV = fullResUV + vec2(float(dx), float(dy)) * texelSize;
            
            // Bounds check
            if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || 
                sampleUV.y < 0.0 || sampleUV.y > 1.0) {
                continue;
            }
            
            float sampleDepthRaw = texture(primaryDepth, sampleUV).r;
            float sampleDepth = lumonLinearizeDepth(sampleDepthRaw, zNear, zFar);
            vec3 sampleNormal = lumonDecodeNormal(texture(gBufferNormal, sampleUV).xyz);
            
            // Spatial weight (Gaussian)
            float dist = length(vec2(float(dx), float(dy)));
            float spatialWeight = exp(-dist * dist / 
                                      (2.0 * upsampleSpatialSigma * upsampleSpatialSigma));
            
            // Depth weight
            float depthDiff = abs(centerDepth - sampleDepth) / max(centerDepth, 0.01);
            float depthWeight = exp(-depthDiff * depthDiff / 
                                    (2.0 * upsampleDepthSigma * upsampleDepthSigma));
            
            // Normal weight
            float normalDot = max(dot(centerNormal, sampleNormal), 0.0);
            float normalWeight = pow(normalDot, upsampleNormalSigma);
            
            float weight = spatialWeight * depthWeight * normalWeight;
            
            // Sample from half-res (mapped to neighbor location)
            ivec2 halfResCoord = ivec2(sampleUV * halfResSize);
            vec3 sampleColor = texelFetch(indirectHalf, halfResCoord, 0).rgb;
            
            result += sampleColor * weight;
            totalWeight += weight;
        }
    }
    
    return result / totalWeight;
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    vec2 fullResUV = gl_FragCoord.xy / screenSize;
    
    // Sample center depth and normal
    float centerDepthRaw = texture(primaryDepth, fullResUV).r;
    
    // Early out for sky
    if (lumonIsSky(centerDepthRaw)) {
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    
    float centerDepth = lumonLinearizeDepth(centerDepthRaw, zNear, zFar);
    vec3 centerNormal = lumonDecodeNormal(texture(gBufferNormal, fullResUV).xyz);
    
    vec3 result;
    
    if (denoiseEnabled == 1) {
        // Bilateral upsample from half-res with edge-aware filtering
        result = bilateralUpsample(fullResUV, centerDepth, centerNormal);
        
        // Optional: Apply additional spatial denoise for very noisy areas
        // Uncomment if needed for specific scenarios
        // result = spatialDenoise(fullResUV, result, centerDepth, centerNormal);
    } else {
        // Simple bilinear sample (faster but less edge-aware)
        result = texture(indirectHalf, fullResUV).rgb;
    }
    
    outColor = vec4(result, 1.0);
}
