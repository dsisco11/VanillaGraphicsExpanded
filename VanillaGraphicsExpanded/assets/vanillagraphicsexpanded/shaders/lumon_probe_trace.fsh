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
@import "lumon_common.fsh"

// Import SH helpers
@import "lumon_sh.fsh"

// Import noise for ray jittering
@import "squirrel3.fsh"

// Probe anchor textures (world-space for temporal stability)
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalWS.xyz, reserved

// Scene textures for ray marching
uniform sampler2D primaryDepth;
uniform sampler2D primaryColor;

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
uniform int raysPerProbe;
uniform int raySteps;
uniform float rayMaxDistance;
uniform float rayThickness;

// Z-planes
uniform float zNear;
uniform float zFar;

// Sky fallback
uniform float skyMissWeight;
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

/**
 * Distance falloff function for inverse-square attenuation.
 * Uses an offset to avoid division by zero and provide soft falloff.
 */
float distanceFalloff(float dist) {
    return 1.0 / (1.0 + dist * dist);
}

RayHit traceRay(vec3 origin, vec3 direction) {
    RayHit result;
    result.hit = false;
    result.position = vec3(0.0);
    result.color = vec3(0.0);
    result.distance = rayMaxDistance;
    
    float stepSize = rayMaxDistance / float(raySteps);
    
    for (int i = 1; i <= raySteps; i++) {
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
        
        if (depthDiff > 0.0 && depthDiff < rayThickness) {
            // Hit!
            result.hit = true;
            result.position = scenePos;
            result.color = texture(primaryColor, sampleUV).rgb;
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
    
    // Generate seed for this probe and frame
    uint seed = uint(probeCoord.x + probeCoord.y * int(probeGridSize.x) + frameIndex * int(probeGridSize.x * probeGridSize.y));
    
    // Trace rays
    float weightSum = 0.0;
    
    for (int i = 0; i < raysPerProbe; i++) {
        // Generate jittered random values for this ray
        float u1 = Squirrel3HashF(seed + uint(i * 2));
        float u2 = Squirrel3HashF(seed + uint(i * 2 + 1));
        
        // Generate cosine-weighted ray direction
        vec3 rayDir = lumonCosineSampleHemisphere(vec2(u1, u2), probeNormal);
        
        // Offset origin slightly to avoid self-intersection
        vec3 rayOrigin = probePos + probeNormal * 0.01;
        
        // Trace ray
        RayHit hit = traceRay(rayOrigin, rayDir);
        
        vec3 radiance;
        if (hit.hit) {
            // Apply indirect tint and distance falloff to bounced radiance
            radiance = hit.color * indirectTint * distanceFalloff(hit.distance);
        } else {
            // Sky fallback
            radiance = lumonGetSkyColor(rayDir, sunPosition, sunColor, ambientColor, skyMissWeight);
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
