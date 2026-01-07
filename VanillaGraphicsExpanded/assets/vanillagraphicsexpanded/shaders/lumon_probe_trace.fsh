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

// Import SH helpers
@import "lumon_sh.fsh"

// Import noise for ray jittering
@import "squirrel3.fsh"

// Constants
const float PI = 3.141592653589793;
const float TAU = 6.283185307179586;
const float GOLDEN_ANGLE = 2.399963229728653;

// ============================================================================
// Utility Functions
// ============================================================================

float linearizeDepth(float depth) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

vec3 reconstructViewPos(vec2 texCoord, float depth) {
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = invProjectionMatrix * ndc;
    return viewPos.xyz / viewPos.w;
}

vec2 projectToScreen(vec3 viewPos) {
    vec4 clipPos = projectionMatrix * vec4(viewPos, 1.0);
    return (clipPos.xy / clipPos.w) * 0.5 + 0.5;
}

// Generate cosine-weighted hemisphere direction
vec3 cosineSampleHemisphere(vec2 u, vec3 normal) {
    // Generate uniform disk sample
    float r = sqrt(u.x);
    float theta = TAU * u.y;
    float x = r * cos(theta);
    float y = r * sin(theta);
    float z = sqrt(max(0.0, 1.0 - u.x));
    
    // Build tangent frame
    vec3 tangent = abs(normal.y) < 0.999 
        ? normalize(cross(vec3(0.0, 1.0, 0.0), normal))
        : normalize(cross(vec3(1.0, 0.0, 0.0), normal));
    vec3 bitangent = cross(normal, tangent);
    
    // Transform to world/view space
    return normalize(tangent * x + bitangent * y + normal * z);
}

// Get sky color for a ray direction
vec3 getSkyColor(vec3 rayDir) {
    float skyFactor = max(0.0, rayDir.y) * 0.5 + 0.5;
    vec3 skyColor = ambientColor * skyFactor;
    
    // Add sun contribution
    float sunDot = max(0.0, dot(rayDir, normalize(sunPosition)));
    skyColor += sunColor * pow(sunDot, 32.0) * 0.5;
    
    return skyColor * skyMissWeight;
}

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
        vec2 sampleUV = projectToScreen(samplePos);
        
        // Check bounds
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0) {
            continue;
        }
        
        // Sample depth
        float sceneDepth = texture(primaryDepth, sampleUV).r;
        vec3 scenePos = reconstructViewPos(sampleUV, sceneDepth);
        
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
    vec2 probeUV = (vec2(probeCoord) + 0.5) / vec2(probeGridSize);
    
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
    uint seed = uint(probeCoord.x + probeCoord.y * probeGridSize.x + frameIndex * probeGridSize.x * probeGridSize.y);
    
    // Trace rays
    float weightSum = 0.0;
    
    for (int i = 0; i < raysPerProbe; i++) {
        // Generate jittered random values for this ray
        float u1 = squirrel3Float(seed + uint(i * 2));
        float u2 = squirrel3Float(seed + uint(i * 2 + 1));
        
        // Generate cosine-weighted ray direction
        vec3 rayDir = cosineSampleHemisphere(vec2(u1, u2), probeNormal);
        
        // Offset origin slightly to avoid self-intersection
        vec3 rayOrigin = probePos + probeNormal * 0.01;
        
        // Trace ray
        RayHit hit = traceRay(rayOrigin, rayDir);
        
        vec3 radiance;
        if (hit.hit) {
            radiance = hit.color;
        } else {
            // Sky fallback
            radiance = getSkyColor(rayDir);
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
