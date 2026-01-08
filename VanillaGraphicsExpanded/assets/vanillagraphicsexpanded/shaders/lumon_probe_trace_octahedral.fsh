#version 330 core

in vec2 uv;

// Single output to octahedral atlas
out vec4 outRadiance;  // RGB = radiance, A = log-encoded hit distance

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
@import "lumon_common.fsh"

// Import octahedral mapping
@import "lumon_octahedral.glsl"

// Import noise for ray jittering
@import "squirrel3.fsh"

// Probe anchor textures (world-space)
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalWS.xyz, reserved

// Scene textures for ray marching
uniform sampler2D primaryDepth;
uniform sampler2D primaryColor;

// Matrices
uniform mat4 invProjectionMatrix;
uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;       // For world-space to view-space transform
uniform mat4 invViewMatrix;    // For view-space to world-space transform

// Probe grid parameters
uniform vec2 probeGridSize;    // (probeCountX, probeCountY) as float
uniform vec2 screenSize;

// Temporal distribution
uniform int frameIndex;
uniform int texelsPerFrame;    // How many octahedral texels to trace per frame (default 8)

// Ray tracing parameters
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

// History for temporal blending (read from previous frame)
uniform sampler2D octahedralHistory;

// ============================================================================
// Temporal Distribution
// ============================================================================

/**
 * Determine if this texel should be traced this frame.
 * Uses frame index and texel position to distribute tracing over time.
 */
bool shouldTraceThisFrame(ivec2 octTexel, int probeIndex) {
    // Linear texel index within the probe's 8×8 tile
    int texelIndex = octTexel.y * LUMON_OCTAHEDRAL_SIZE + octTexel.x;
    
    // Which "batch" does this texel belong to?
    // With 64 texels and 8 texels/frame, there are 8 batches
    int numBatches = (LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE) / texelsPerFrame;
    int batch = texelIndex / texelsPerFrame;
    
    // Add per-probe jitter to avoid all probes tracing the same texels
    int jitteredFrame = (frameIndex + probeIndex) % numBatches;
    
    return batch == jitteredFrame;
}

// ============================================================================
// Ray Marching (view-space for depth buffer compatibility)
// ============================================================================

struct RayHit {
    bool hit;
    vec3 color;
    float distance;
};

/**
 * Distance falloff function for inverse-square attenuation.
 */
float distanceFalloff(float dist) {
    return 1.0 / (1.0 + dist * dist);
}

/**
 * Trace a ray in view-space and return hit information.
 */
RayHit traceRay(vec3 originVS, vec3 directionVS) {
    RayHit result;
    result.hit = false;
    result.color = vec3(0.0);
    result.distance = rayMaxDistance;
    
    float stepSize = rayMaxDistance / float(raySteps);
    
    for (int i = 1; i <= raySteps; i++) {
        float t = stepSize * float(i);
        vec3 samplePos = originVS + directionVS * t;
        
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
        float depthDiff = scenePos.z - samplePos.z;
        
        if (depthDiff > 0.0 && depthDiff < rayThickness) {
            // Hit!
            result.hit = true;
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
    if (!shouldTraceThisFrame(octTexel, probeIndex)) {
        // Keep history value - don't trace this frame
        vec2 atlasUV = (vec2(atlasCoord) + 0.5) / (probeGridSize * float(LUMON_OCTAHEDRAL_SIZE));
        outRadiance = texture(octahedralHistory, atlasUV);
        return;
    }
    
    // Load probe anchor data (stored in world-space)
    vec4 anchorData = texelFetch(probeAnchorPosition, probeCoord, 0);
    vec3 probePosWS = anchorData.xyz;
    float valid = anchorData.w;
    
    // If probe is invalid, output zero radiance
    if (valid < 0.5) {
        outRadiance = vec4(0.0, 0.0, 0.0, 0.0);
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
    
    if (hit.hit) {
        // Apply indirect tint and distance falloff to bounced radiance
        radiance = hit.color * indirectTint * distanceFalloff(hit.distance);
        hitDistance = hit.distance;
    } else {
        // Sky fallback - use world-space direction for consistent sky color
        radiance = lumonGetSkyColor(rayDirWS, sunPosition, sunColor, ambientColor, skyMissWeight);
        hitDistance = rayMaxDistance;
    }
    
    // Encode hit distance for storage
    float encodedHitDist = lumonEncodeHitDistance(hitDistance);
    
    // Output radiance + hit distance
    outRadiance = vec4(radiance, encodedHitDist);
}
