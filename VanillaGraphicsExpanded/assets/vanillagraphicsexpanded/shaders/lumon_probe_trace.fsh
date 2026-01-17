#version 330 core

// MRT outputs for SH radiance cache
layout(location = 0) out vec4 outRadiance0;  // SH coefficients set 0
layout(location = 1) out vec4 outRadiance1;  // SH coefficients set 1

// ============================================================================
// LumOn Probe Trace Pass
// 
// Traces rays from each probe and accumulates radiance into SH L1 coefficients.
// Uses screen-space ray marching with jittered ray directions per frame.
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import global defines (loop-bound knobs)
@import "./includes/vge_global_defines.glsl"

// Import SH helpers
@import "./includes/lumon_sh.glsl"

// PMJ temporal jitter sequence (provided by CPU as an RG16_UNorm 1xN texture)

// Probe anchor textures (world-space for temporal stability)
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalWS.xyz, reserved

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

// Matrices
uniform mat4 invProjectionMatrix;
uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;       // For world-space to view-space transform

// Probe grid parameters
uniform int probeSpacing;
uniform vec2 probeGridSize;
uniform vec2 screenSize;

// Ray tracing parameters
uniform int frameIndex;

uniform sampler2D pmjJitter;
uniform int pmjCycleLength;

// Z-planes
uniform float zNear;
uniform float zFar;

// Sky fallback
uniform vec3 sunPosition;
uniform vec3 sunColor;
uniform vec3 ambientColor;

// Indirect lighting tuning
uniform vec3 indirectTint;

// ============================================================================
// Ray Marching
// ============================================================================

struct RayHit {
    bool hit;
    vec3 position;
    vec3 color;
    float distance;
};

RayHit traceRay(vec3 origin, vec3 direction) {
    RayHit result;
    result.hit = false;
    result.position = vec3(0.0);
    result.color = vec3(0.0);
    result.distance = VGE_LUMON_RAY_MAX_DISTANCE;
    
    float stepSize = VGE_LUMON_RAY_MAX_DISTANCE / float(VGE_LUMON_RAY_STEPS);
    
    for (int i = 1; i <= VGE_LUMON_RAY_STEPS; i++) {
        float t = stepSize * float(i);
        vec3 samplePos = origin + direction * t;
        
        // Project to screen
        vec2 sampleUV = lumonProjectToScreen(samplePos, projectionMatrix);
        
        // Check bounds - break early if ray exits screen
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0) {
            break;
        }
        
        // Sample depth
        float sceneDepth = texture(primaryDepth, sampleUV).r;
        
        // Skip sky regions (no valid geometry to hit)
        if (lumonIsSky(sceneDepth)) {
            continue;
        }
        
        vec3 scenePos = lumonReconstructViewPos(sampleUV, sceneDepth, invProjectionMatrix);
        
        // Depth test with thickness
        // In view-space, Z is negative (into screen). Ray behind scene when rayZ < sceneZ.
        // depthDiff > 0 means ray is behind scene (scenePos.z is less negative = closer to camera)
        float depthDiff = scenePos.z - samplePos.z;
        
        if (depthDiff > 0.0 && depthDiff < VGE_LUMON_RAY_THICKNESS) {
            // Hit!
            result.hit = true;
            result.position = scenePos;
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
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    
    // Load probe anchor data (stored in world-space for temporal stability)
    vec4 anchorData = texelFetch(probeAnchorPosition, probeCoord, 0);
    vec3 probePosWS = anchorData.xyz;
    float valid = anchorData.w;
    
    // If probe is invalid, output zero radiance
    if (valid < 0.5) {
        outRadiance0 = vec4(0.0);
        outRadiance1 = vec4(0.0);
        return;
    }
    
    // Read and decode probe normal (stored as [0,1], need [-1,1])
    vec3 probeNormalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    
    // Transform to view-space for screen-space ray marching
    // (depth buffer comparisons require view-space coordinates)
    vec3 probePos = (viewMatrix * vec4(probePosWS, 1.0)).xyz;
    vec3 probeNormal = normalize(mat3(viewMatrix) * probeNormalWS);
    
    // Initialize SH accumulators
    vec4 shR = vec4(0.0);
    vec4 shG = vec4(0.0);
    vec4 shB = vec4(0.0);
    
    // Linear probe index for per-probe decorrelation.
    int probeIndex = probeCoord.x + probeCoord.y * int(probeGridSize.x);
    
    // Trace rays
    float weightSum = 0.0;
    
    for (int i = 0; i < VGE_LUMON_RAYS_PER_PROBE; i++) {
        // PMJ per-ray jitter: index across frames and rays for progressive temporal distribution.
        vec2 u = vec2(0.5);
        if (pmjCycleLength > 0) {
            int idx = (frameIndex * VGE_LUMON_RAYS_PER_PROBE + i + probeIndex) % pmjCycleLength;
            u = texelFetch(pmjJitter, ivec2(idx, 0), 0).rg;
        }
        float u1 = u.x;
        float u2 = u.y;
        
        // Generate cosine-weighted ray direction
        vec3 rayDir = lumonCosineSampleHemisphere(vec2(u1, u2), probeNormal);
        
        // Offset origin slightly to avoid self-intersection
        vec3 rayOrigin = probePos + probeNormal * 0.01;
        
        // Trace ray
        RayHit hit = traceRay(rayOrigin, rayDir);
        
        vec3 radiance;
        if (hit.hit) {
            // Hit radiance (no distance falloff; rayMaxDistance is the only cutoff)
            radiance = hit.color * indirectTint;
        } else {
            // Sky fallback
            radiance = lumonGetSkyColor(rayDir, sunPosition, sunColor, ambientColor, VGE_LUMON_SKY_MISS_WEIGHT);
        }
        
        // Accumulate into SH
        float weight = 1.0;
        shProjectRGB(shR, shG, shB, rayDir, radiance, weight);
        weightSum += weight;
    }
    
    // Normalize by sample count
    if (weightSum > 0.0) {
        float invWeight = 1.0 / weightSum;
        shR *= invWeight;
        shG *= invWeight;
        shB *= invWeight;
    }
    
    // Clamp to prevent negative reconstruction
    shClampNegative(shR, shG, shB);
    
    // Pack into output textures
    shPackToTextures(shR, shG, shB, outRadiance0, outRadiance1);
}
