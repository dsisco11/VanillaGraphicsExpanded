#version 330 core

in vec2 uv;

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
@import "lumon_common.ash"

// Import SH helpers
@import "lumon_sh.fsh"

// Import noise for ray jittering
@import "squirrel3.fsh"

// Probe anchor textures
uniform sampler2D probeAnchorPosition;  // posVS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalVS.xyz, reserved

// Scene textures for ray marching
uniform sampler2D primaryDepth;
uniform sampler2D primaryColor;

// Matrices
uniform mat4 invProjectionMatrix;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

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

// ============================================================================
// Ray Marching
// ============================================================================

struct RayHit {
    bool hit;
    vec3 position;
    vec3 color;
};

RayHit traceRay(vec3 origin, vec3 direction) {
    RayHit result;
    result.hit = false;
    result.position = vec3(0.0);
    result.color = vec3(0.0);
    
    float stepSize = rayMaxDistance / float(raySteps);
    
    for (int i = 1; i <= raySteps; i++) {
        float t = stepSize * float(i);
        vec3 samplePos = origin + direction * t;
        
        // Project to screen
        vec2 sampleUV = lumonProjectToScreen(samplePos, projectionMatrix);
        
        // Check bounds
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0) {
            continue;
        }
        
        // Sample depth
        float sceneDepth = texture(primaryDepth, sampleUV).r;
        vec3 scenePos = lumonReconstructViewPos(sampleUV, sceneDepth, invProjectionMatrix);
        
        // Depth test with thickness
        float depthDiff = samplePos.z - scenePos.z;
        
        if (depthDiff > 0.0 && depthDiff < rayThickness) {
            // Hit!
            result.hit = true;
            result.position = scenePos;
            result.color = texture(primaryColor, sampleUV).rgb;
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
    vec2 probeUV = lumonProbeCoordToUV(probeCoord, probeGridSize);
    
    // Read probe anchor
    vec4 anchorData = texture(probeAnchorPosition, probeUV);
    vec3 probePos = anchorData.xyz;
    float valid = anchorData.w;
    
    // If probe is invalid, output zero radiance
    if (valid < 0.5) {
        outRadiance0 = vec4(0.0);
        outRadiance1 = vec4(0.0);
        return;
    }
    
    // Read probe normal
    vec3 probeNormal = texture(probeAnchorNormal, probeUV).xyz;
    
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
        float u1 = squirrel3Float(seed + uint(i * 2));
        float u2 = squirrel3Float(seed + uint(i * 2 + 1));
        
        // Generate cosine-weighted ray direction
        vec3 rayDir = lumonCosineSampleHemisphere(vec2(u1, u2), probeNormal);
        
        // Offset origin slightly to avoid self-intersection
        vec3 rayOrigin = probePos + probeNormal * 0.01;
        
        // Trace ray
        RayHit hit = traceRay(rayOrigin, rayDir);
        
        vec3 radiance;
        if (hit.hit) {
            radiance = hit.color;
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
    
    // Pack into output textures
    shPackToTextures(shR, shG, shB, outRadiance0, outRadiance1);
}
