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
@import "./includes/lumon_common.glsl"

// Import global defines (feature toggles with defaults)
@import "./includes/vge_global_defines.glsl"

// Phase 23: shared per-frame state via UBOs.
#define LUMON_UBO_ENABLE_ALIASES
@import "./includes/lumon_ubos.glsl"

// Half-resolution indirect diffuse
uniform sampler2D indirectHalf;

// G-buffer for edge detection
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Quality parameters (from spec Section 3.1)
uniform float upsampleDepthSigma;   // e.g., 0.1
uniform float upsampleNormalSigma;  // e.g., 16.0
uniform float upsampleSpatialSigma; // e.g., 1.0

// Hole filling (Phase 14 - bounded fallback)
// Uses the alpha channel of indirectHalf as a confidence/quality metric.
uniform int holeFillRadius;           // half-res pixel radius (e.g., 2)
uniform float holeFillMinConfidence;  // minimum neighbor confidence to use (e.g., 0.05)

// ============================================================================
// Bilateral Upsample
// ============================================================================

/**
 * Bilateral upsample from half-res to full-res with edge-awareness.
 * Uses 2x2 kernel in half-res space with depth/normal guided weights.
 */
vec3 bilateralUpsample(vec2 fullResUV, float centerDepth, vec3 centerNormal) {
    // UE-style plane weighting: evaluate distance-to-plane in view space.
    ivec2 maxFull = ivec2(screenSize) - 1;
    ivec2 centerPx = clamp(ivec2(fullResUV * screenSize), ivec2(0), maxFull);
    float centerDepthRaw = texelFetch(primaryDepth, centerPx, 0).r;
    vec3 centerPosVS = lumonReconstructViewPos(fullResUV, centerDepthRaw, invProjectionMatrix);
    float centerDepthVS = max(-centerPosVS.z, 1.0);

    vec3 centerNormalVS = normalize(mat3(viewMatrix) * centerNormal);
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

            // Sample guides at corresponding full-res location (robust 2x2 selection)
            ivec2 bestFull;
            float sampleDepthRaw;
            vec3 sampleNormal;
            if (!lumonSelectGuidesForHalfResCoord(sampleCoord, primaryDepth, gBufferNormal, ivec2(screenSize), bestFull, sampleDepthRaw, sampleNormal))
            {
                continue;
            }

            float sampleDepth = lumonLinearizeDepth(sampleDepthRaw, zNear, zFar);

            vec2 sampleUV = (vec2(bestFull) + 0.5) / screenSize;
            vec3 samplePosVS = lumonReconstructViewPos(sampleUV, sampleDepthRaw, invProjectionMatrix);
            
            // Bilinear weight
            float bx = (dx == 0) ? (1.0 - fracCoord.x) : fracCoord.x;
            float by = (dy == 0) ? (1.0 - fracCoord.y) : fracCoord.y;
            float bilinearWeight = bx * by;
            
            // Plane-weighted depth similarity (more stable at silhouettes)
            float planeDist = abs(dot(samplePosVS - centerPosVS, centerNormalVS));
            float planeSigma = max(upsampleDepthSigma, 1e-3) * centerDepthVS;
            float depthWeight = exp(-(planeDist * planeDist) / (2.0 * planeSigma * planeSigma));
            
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
    } else {
        // Never output black due to edge rejection; fall back to simple sampling.
        result = texture(indirectHalf, fullResUV).rgb;
    }
    
    return result;
}

// ============================================================================
// Hole Fill (Low-Confidence Neighborhood Resolve)
// ============================================================================

/**
 * Fills holes (low-confidence half-res samples) using a bounded, edge-aware neighborhood
 * resolve in half-res space.
 *
 * This is intended as a controlled fallback for missing/invalid indirect values.
 * It never overrides high-confidence samples.
 */
vec3 holeFillResolve(vec2 fullResUV, float centerDepth, vec3 centerNormal)
{
    ivec2 centerHalf = ivec2(fullResUV * halfResSize);
    centerHalf = clamp(centerHalf, ivec2(0), ivec2(halfResSize) - 1);

    vec3 accum = vec3(0.0);
    float totalW = 0.0;

    // Use the same spatial sigma as the optional denoise kernel.
    float spatialSigma = max(upsampleSpatialSigma, 1e-3);

    for (int dy = -8; dy <= 8; dy++)
    {
        if (dy < -holeFillRadius || dy > holeFillRadius) continue;
        for (int dx = -8; dx <= 8; dx++)
        {
            if (dx < -holeFillRadius || dx > holeFillRadius) continue;

            ivec2 sampleCoord = centerHalf + ivec2(dx, dy);
            sampleCoord = clamp(sampleCoord, ivec2(0), ivec2(halfResSize) - 1);

            vec4 halfSample = texelFetch(indirectHalf, sampleCoord, 0);
            float conf = halfSample.a;
            if (conf < holeFillMinConfidence) continue;

            ivec2 bestFull;
            float sampleDepthRaw;
            vec3 sampleNormal;
            if (!lumonSelectGuidesForHalfResCoord(sampleCoord, primaryDepth, gBufferNormal, ivec2(screenSize), bestFull, sampleDepthRaw, sampleNormal))
            {
                continue;
            }

            float sampleDepth = lumonLinearizeDepth(sampleDepthRaw, zNear, zFar);

            float dist = length(vec2(float(dx), float(dy)));
            float spatialW = exp(-dist * dist / (2.0 * spatialSigma * spatialSigma));

            // Hole-fill should be permissive: prefer a valid non-black fill over strict edge rejection.
            float depthDenom = max(max(centerDepth, sampleDepth), 1.0);
            float depthDiff = abs(centerDepth - sampleDepth) / depthDenom;
            float depthW = exp(-depthDiff * depthDiff / (2.0 * upsampleDepthSigma * upsampleDepthSigma));

            float normalDot = max(dot(centerNormal, sampleNormal), 0.0);
            float normalW = pow(normalDot, upsampleNormalSigma);

            float w = spatialW * depthW * normalW * conf;
            accum += halfSample.rgb * w;
            totalW += w;
        }
    }

    if (totalW > 1e-6)
    {
        return accum / totalW;
    }

    // If we couldn't find any valid neighbors (e.g., very thin silhouettes),
    // fall back to the standard bilateral upsample instead of returning black.
    return bilateralUpsample(fullResUV, centerDepth, centerNormal);
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
    ivec2 maxFull = ivec2(screenSize) - 1;
    
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            if (dx == 0 && dy == 0) continue;
            
            vec2 sampleUV = fullResUV + vec2(float(dx), float(dy)) * texelSize;
            
            // Bounds check
            if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || 
                sampleUV.y < 0.0 || sampleUV.y > 1.0) {
                continue;
            }
            
            ivec2 samplePx = clamp(ivec2(sampleUV * screenSize), ivec2(0), maxFull);
            float sampleDepthRaw = texelFetch(primaryDepth, samplePx, 0).r;
            float sampleDepth = lumonLinearizeDepth(sampleDepthRaw, zNear, zFar);
            vec3 sampleNormal = lumonDecodeNormal(texelFetch(gBufferNormal, samplePx, 0).xyz);
            
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
    ivec2 maxFull = ivec2(screenSize) - 1;
    ivec2 centerPx = clamp(ivec2(gl_FragCoord.xy), ivec2(0), maxFull);
    float centerDepthRaw = texelFetch(primaryDepth, centerPx, 0).r;
    
    // Early out for sky
    if (lumonIsSky(centerDepthRaw)) {
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    
    float centerDepth = lumonLinearizeDepth(centerDepthRaw, zNear, zFar);
    vec3 centerNormal = lumonDecodeNormal(texelFetch(gBufferNormal, centerPx, 0).xyz);

    // Low-confidence detection comes from the half-res gather output alpha.
    ivec2 centerHalf = ivec2(fullResUV * halfResSize);
    centerHalf = clamp(centerHalf, ivec2(0), ivec2(halfResSize) - 1);
    float centerConf = texelFetch(indirectHalf, centerHalf, 0).a;
    
    vec3 result;

#if VGE_LUMON_UPSAMPLE_HOLEFILL
    if (centerConf < holeFillMinConfidence) {
        // Controlled fallback: only affect low-confidence pixels.
        result = holeFillResolve(fullResUV, centerDepth, centerNormal);
    }
    else
#endif
#if VGE_LUMON_UPSAMPLE_DENOISE
    {
        // Bilateral upsample from half-res with edge-aware filtering
        result = bilateralUpsample(fullResUV, centerDepth, centerNormal);
        
        // Optional: Apply additional spatial denoise for very noisy areas
        // Uncomment if needed for specific scenarios
        result = spatialDenoise(fullResUV, result, centerDepth, centerNormal);
    }
#else
    {
        // Simple bilinear sample (faster but less edge-aware)
        result = texture(indirectHalf, fullResUV).rgb;
    }
#endif
    
    outColor = vec4(result, 1.0);
}
