#version 330 core

// MRT output to probe atlas
layout(location = 0) out vec4 outRadiance;  // RGB = radiance, A = log-encoded hit distance
layout(location = 1) out vec2 outMeta;      // R = confidence, G = uintBitsToFloat(flags)

// ============================================================================
// LumOn Octahedral Probe Trace Pass
// 
// Traces rays from probes and stores radiance + hit distance in octahedral maps.
// Uses 2D atlas layout for GL 3.3 compatibility: (probeCountX×8, probeCountY×8)
// Each probe's 8×8 octahedral tile is tiled into the atlas.
//
// Temporal distribution: Only updates a subset of texels each frame.
// Over 8 frames, all 64 directions per probe are updated.
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import global defines (loop-bound knobs)
@import "./includes/vge_global_defines.glsl"

// Import octahedral mapping
@import "./includes/lumon_octahedral.glsl"

// Import probe-atlas meta helpers
@import "./includes/lumon_probe_atlas_meta.glsl"

// World-probe clipmap helpers
@import "./includes/lumon_worldprobe.glsl"

// Import noise for ray jittering
@import "./includes/squirrel3.glsl"

// Phase 23: shared per-frame state via UBOs is imported by lumon_worldprobe.glsl.

// Probe anchor textures (world-space)
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalWS.xyz, reserved

// Phase 10: probe-resolution trace mask (RG32F packed uint bits).
// When present and valid, it replaces legacy batch slicing.
uniform sampler2D probeTraceMask;

// Scene textures for ray marching
uniform sampler2D primaryDepth;
// Radiance sources (linear, pre-tonemap HDR)
uniform sampler2D directDiffuse;
uniform sampler2D emissive;

// Emissive GI scaling (boost emissive as an indirect light source)
// Wired via VGE's runtime define system (VgeShaderProgram.SetDefine).
#ifndef LUMON_EMISSIVE_BOOST
#define LUMON_EMISSIVE_BOOST 1.0
#endif

// Optional HZB depth pyramid
uniform sampler2D hzbDepth;

// Indirect lighting tuning
uniform vec3 indirectTint;

// History for temporal blending (read from previous frame)
uniform sampler2D octahedralHistory;

// History meta for temporal preservation (read from previous frame)
uniform sampler2D probeAtlasMetaHistory;

// ============================================================================
// Temporal Distribution
// ============================================================================

/**
 * Determine if this texel should be traced this frame.
 * Uses frame index and texel position to distribute tracing over time.
 */
bool legacyShouldTraceThisFrame(ivec2 octTexel, int probeIndex) {
    // Linear texel index within the probe's 8×8 tile
    int texelIndex = octTexel.y * LUMON_OCTAHEDRAL_SIZE + octTexel.x;
    
    // Which "batch" does this texel belong to?
    // With 64 texels and 8 texels/frame, there are 8 batches
    int numBatches = (LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE) / VGE_LUMON_ATLAS_TEXELS_PER_FRAME;
    int batch = texelIndex / VGE_LUMON_ATLAS_TEXELS_PER_FRAME;
    
    // Add per-probe jitter to avoid all probes tracing the same texels
    int jitteredFrame = (frameIndex + probeIndex) % numBatches;
    
    return batch == jitteredFrame;
}

bool maskIsValid(uvec2 maskBits)
{
    // An all-zero mask should never happen in the real mask pass (it must select K>0 texels).
    // Treat it as "mask invalid" so we fall back to the legacy batch slicing.
    return (maskBits.x | maskBits.y) != 0u;
}

bool maskShouldTraceTexel(uvec2 maskBits, int texelIndex)
{
    texelIndex = clamp(texelIndex, 0, (LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE) - 1);
    if (texelIndex < 32)
    {
        return ((maskBits.x >> uint(texelIndex)) & 1u) != 0u;
    }
    return ((maskBits.y >> uint(texelIndex - 32)) & 1u) != 0u;
}

// ============================================================================
// Ray Marching (view-space for depth buffer compatibility)
// ============================================================================

struct RayHit {
    bool hit;
    bool exitedScreen;
    vec3 color;
    float distance;
};

/**
 * Trace a ray in view-space and return hit information.
 */
RayHit traceRay(vec3 originVS, vec3 directionVS) {
    RayHit result;
    result.hit = false;
    result.exitedScreen = false;
    result.color = vec3(0.0);
    result.distance = VGE_LUMON_RAY_MAX_DISTANCE;
    
    float stepSize = VGE_LUMON_RAY_MAX_DISTANCE / float(VGE_LUMON_RAY_STEPS);
    
    for (int i = 1; i <= VGE_LUMON_RAY_STEPS; i++) {
        float t = stepSize * float(i);
        vec3 samplePos = originVS + directionVS * t;
        
        // Project to screen
        vec2 sampleUV = lumonProjectToScreen(samplePos, projectionMatrix);
        
        // Check bounds - break early if ray exits screen
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0) {
            result.exitedScreen = true;
            break;
        }
        
        // HZB coarse rejection (reduces full-res depth sampling)
        int mip = max(0, VGE_LUMON_HZB_COARSE_MIP);
        ivec2 hzbSize = textureSize(hzbDepth, mip);
        ivec2 hzbCoord = clamp(ivec2(sampleUV * vec2(hzbSize)), ivec2(0), hzbSize - 1);
        float coarseDepth = texelFetch(hzbDepth, hzbCoord, mip).r;

        if (!lumonIsSky(coarseDepth)) {
            vec4 clip = projectionMatrix * vec4(samplePos, 1.0);
            float ndcZ = clip.z / max(1e-6, clip.w);
            float sampleDepthRaw = ndcZ * 0.5 + 0.5;

            // If the ray sample is closer than the nearest depth in this region,
            // we can skip the expensive full-res depth test for this step.
            if (sampleDepthRaw + 1e-5 < coarseDepth) {
                continue;
            }
        }

        // Sample full-res depth
        float sceneDepth = texture(primaryDepth, sampleUV).r;
        
        // Skip sky regions (no valid geometry to hit)
        if (lumonIsSky(sceneDepth)) {
            continue;
        }
        
        vec3 scenePos = lumonReconstructViewPos(sampleUV, sceneDepth, invProjectionMatrix);
        
        // Depth test with thickness
        float depthDiff = scenePos.z - samplePos.z;
        
        if (depthDiff > 0.0 && depthDiff < VGE_LUMON_RAY_THICKNESS) {
            // Hit!
            result.hit = true;
            vec3 direct = texture(directDiffuse, sampleUV).rgb;
            vec3 em = texture(emissive, sampleUV).rgb * LUMON_EMISSIVE_BOOST;
            result.color = direct + em;
            result.distance = t;
            return result;
        }
    }
    
    return result;
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Calculate which probe and octahedral texel this fragment corresponds to
    ivec2 atlasCoord = ivec2(gl_FragCoord.xy);
    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;
    ivec2 octTexel = atlasCoord % LUMON_OCTAHEDRAL_SIZE;
    
    // Convert probeGridSize to ivec2 for integer operations
    ivec2 probeGridSizeI = ivec2(probeGridSize);
    
    // Clamp probe coord to valid range
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSizeI - 1);
    
    // Linear probe index for jitter
    int probeIndex = probeCoord.y * probeGridSizeI.x + probeCoord.x;
    
    // Check if we should trace this texel this frame (temporal distribution)
    int texelIndex = octTexel.y * LUMON_OCTAHEDRAL_SIZE + octTexel.x;

    // Default to legacy batch slicing.
    bool shouldTrace = legacyShouldTraceThisFrame(octTexel, probeIndex);

    // If PIS is enabled, prefer the probe-resolution mask.
    // Preserve legacy behavior when forced or if the mask appears invalid.
#if (VGE_LUMON_PROBE_PIS_ENABLED == 1) && (VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING == 0)
    vec2 maskPacked = texelFetch(probeTraceMask, probeCoord, 0).xy;
    uvec2 maskBits = uvec2(floatBitsToUint(maskPacked.x), floatBitsToUint(maskPacked.y));

    if (maskIsValid(maskBits))
    {
        shouldTrace = maskShouldTraceTexel(maskBits, texelIndex);
    }
#endif

    if (!shouldTrace) {
        // Keep history value - don't trace this frame
        vec2 atlasUV = (vec2(atlasCoord) + 0.5) / (probeGridSize * float(LUMON_OCTAHEDRAL_SIZE));
        outRadiance = texture(octahedralHistory, atlasUV);
        outMeta = texture(probeAtlasMetaHistory, atlasUV).xy;
        return;
    }
    
    // Load probe anchor data (stored in world-space)
    vec4 anchorData = texelFetch(probeAnchorPosition, probeCoord, 0);
    vec3 probePosWS = anchorData.xyz;
    float valid = anchorData.w;
    
    // If probe is invalid, output zero radiance
    if (valid < 0.5) {
        outRadiance = vec4(0.0, 0.0, 0.0, 0.0);
        outMeta = lumonEncodeMeta(0.0, 0u);
        return;
    }
    
    // Get probe normal (stored in world-space)
    vec3 probeNormalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    
    // Convert octahedral texel to world-space ray direction
    vec2 octUV = lumonTexelCoordToOctahedralUV(octTexel);
    vec3 rayDirWS = lumonOctahedralUVToDirection(octUV);
    
    // Transform probe position and ray direction to view-space for ray marching
    vec3 probePosVS = (viewMatrix * vec4(probePosWS, 1.0)).xyz;
    vec3 rayDirVS = normalize(mat3(viewMatrix) * rayDirWS);
    vec3 probeNormalVS = normalize(mat3(viewMatrix) * probeNormalWS);
    
    // Offset origin slightly to avoid self-intersection
    vec3 rayOriginVS = probePosVS + probeNormalVS * 0.01;
    
    // Trace ray
    RayHit hit = traceRay(rayOriginVS, rayDirVS);
    
    vec3 radiance;
    float hitDistance;
    bool usedWorldProbeFallback = false;
    float worldProbeFallbackConfidence = 0.0;
    
    if (hit.hit) {
        // Hit radiance (no distance falloff; rayMaxDistance is the only cutoff)
        radiance = hit.color * indirectTint;
        hitDistance = hit.distance;
    } else {
        // Miss fallback:
        // - First try world-probes (represents "scene" / off-screen lighting).
        //   This is a directional sample from the world-probe radiance atlas; alpha sign encodes sky visibility.
        // - If unavailable, fall back to the legacy sky approximation.
#if VGE_LUMON_WORLDPROBE_ENABLED
        LumOnWorldProbeRadianceSample wp = lumonWorldProbeSampleClipmapRadianceBound(probePosWS, rayDirWS);
        if (wp.confidence > 1e-3)
        {
            radiance = wp.radiance;
            usedWorldProbeFallback = true;
            worldProbeFallbackConfidence = wp.confidence;
        }
        else
#endif
        {
            // Sky fallback - use world-space direction for consistent sky color
            radiance = lumonGetSkyColor(rayDirWS, sunPosition, sunColor, ambientColor, VGE_LUMON_SKY_MISS_WEIGHT);
        }
        hitDistance = VGE_LUMON_RAY_MAX_DISTANCE;
    }
    
    // Encode hit distance for storage
    float encodedHitDist = lumonEncodeHitDistance(hitDistance);
    
    // Output radiance + hit distance
    outRadiance = vec4(radiance, encodedHitDist);

    // Output meta
    uint flags = 0u;
    float confidence = 0.0;

    if (hit.hit) {
        flags |= LUMON_META_HIT;
        confidence = 1.0;
    } else {
        if (usedWorldProbeFallback)
        {
            flags |= LUMON_META_WORLDPROBE_FALLBACK;
            confidence = worldProbeFallbackConfidence;
        }
        else
        {
            flags |= LUMON_META_SKY_MISS;
            confidence = 0.25;
        }
        if (hit.exitedScreen) {
            flags |= LUMON_META_SCREEN_EXIT;
            if (!usedWorldProbeFallback)
            {
                confidence = 0.05;
            }
        }
    }

    outMeta = lumonEncodeMeta(confidence, flags);
}
