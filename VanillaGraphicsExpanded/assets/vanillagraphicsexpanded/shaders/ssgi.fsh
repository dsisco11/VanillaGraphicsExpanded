#version 330 core

in vec2 uv;
out vec4 outColor;

// ============================================================================
// SSGI - Screen-Space Global Illumination
// 
// Samples indirect lighting from nearby surfaces using screen-space ray marching.
// The SSAO term is multiplied into the indirect lighting for physically correct
// occlusion of bounced light.
//
// NOTE: Temporal reprojection uses depth-only motion estimation. This will cause
// ghosting artifacts on fast-moving entities. Future work should implement:
// - Per-object velocity buffer written during geometry pass
// - Entity motion vector sampling during reprojection
// - Velocity-weighted history rejection for moving objects
// ============================================================================

// Scene textures
uniform sampler2D primaryScene;    // Source of indirect radiance (what surfaces reflect)
uniform sampler2D primaryDepth;    // Depth buffer for position reconstruction
uniform sampler2D gBufferNormal;   // World-space normals for hemisphere orientation
uniform sampler2D ssaoTexture;     // SSAO term in gPosition.a - multiplied into indirect

// Temporal reprojection textures
uniform sampler2D previousSSGI;    // Previous frame's SSGI result
uniform sampler2D previousDepth;   // Previous frame's depth for reprojection

// Matrices
uniform mat4 invProjectionMatrix;  // For view-space position reconstruction
uniform mat4 invModelViewMatrix;   // For world-space position reconstruction
uniform mat4 projectionMatrix;     // For screen-space ray marching
uniform mat4 modelViewMatrix;      // For view-space transformations
uniform mat4 prevViewProjMatrix;   // Previous frame's view-projection for reprojection

// Z-planes
uniform float zNear;
uniform float zFar;

// Frame info
uniform vec2 frameSize;            // Full-resolution frame size
uniform vec2 ssgiBufferSize;       // SSGI buffer size (may be lower res)
uniform float resolutionScale;     // Resolution scale factor (0.25 - 1.0)
uniform int frameIndex;            // Frame counter for temporal jittering

// SSGI parameters
uniform int sampleCount;           // Ray march samples (8-16)
uniform float maxDistance;         // Max ray distance in blocks
uniform float rayThickness;        // Ray thickness for intersection
uniform float intensity;           // Indirect lighting intensity

// Temporal filtering
uniform float temporalBlendFactor; // History blend weight (0.9-0.95)
uniform int temporalEnabled;       // Whether temporal filtering is on

// Constants
const float PI = 3.141592653589793;
const float TAU = 6.283185307179586;
const float GOLDEN_ANGLE = 2.399963229728653;
const float PHI = 1.618033988749895;

// Import Squirrel3 hash for high-quality pseudo-random jittering
@import "squirrel3.fsh"

// ============================================================================
// Utility Functions
// ============================================================================

// Linearize depth from depth buffer (non-linear to linear)
float linearizeDepth(float depth) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

// Reconstruct view-space position from UV and depth
vec3 reconstructViewPos(vec2 texCoord, float depth) {
    // Convert to NDC
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    
    // Transform to view space
    vec4 viewPos = invProjectionMatrix * ndc;
    return viewPos.xyz / viewPos.w;
}

// Project view-space position to screen UV
vec2 projectToScreen(vec3 viewPos) {
    vec4 clipPos = projectionMatrix * vec4(viewPos, 1.0);
    vec3 ndc = clipPos.xyz / clipPos.w;
    return ndc.xy * 0.5 + 0.5;
}

// Cosine-weighted hemisphere sample direction
vec3 sampleHemisphere(vec3 normal, float u1, float u2) {
    // Cosine-weighted hemisphere sampling
    float phi = TAU * u1;
    float cosTheta = sqrt(1.0 - u2);
    float sinTheta = sqrt(u2);
    
    vec3 tangentSample = vec3(
        cos(phi) * sinTheta,
        sin(phi) * sinTheta,
        cosTheta
    );
    
    // Build TBN matrix to orient sample along normal
    vec3 up = abs(normal.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, normal));
    vec3 bitangent = cross(normal, tangent);
    
    return normalize(tangent * tangentSample.x + bitangent * tangentSample.y + normal * tangentSample.z);
}

// ============================================================================
// Screen-Space Ray Marching
// ============================================================================

// Ray march a single direction, returns hit color or black if no hit
vec4 rayMarch(vec3 viewOrigin, vec3 viewDir, float jitter) {
    const int MAX_STEPS = 16;
    float stepSize = maxDistance / float(MAX_STEPS);
    
    // Start slightly offset to avoid self-intersection
    vec3 rayPos = viewOrigin + viewDir * (stepSize * 0.5 + jitter * stepSize);
    
    for (int i = 0; i < MAX_STEPS; i++) {
        // Project to screen space
        vec2 hitUV = projectToScreen(rayPos);
        
        // Check bounds
        if (hitUV.x < 0.0 || hitUV.x > 1.0 || hitUV.y < 0.0 || hitUV.y > 1.0) {
            break; // Ray left screen
        }
        
        // Sample depth at hit point
        float sampledDepth = texture(primaryDepth, hitUV).r;
        float sampledLinearDepth = linearizeDepth(sampledDepth);
        float rayLinearDepth = -rayPos.z; // View space Z is negative forward
        
        // Check for intersection (ray is behind surface)
        float depthDiff = rayLinearDepth - sampledLinearDepth;
        
        if (depthDiff > 0.0 && depthDiff < rayThickness) {
            // Hit! Sample the scene color at this point
            vec3 hitColor = texture(primaryScene, hitUV).rgb;
            
            // Sample SSAO at hit point and multiply into indirect contribution
            // SSAO is stored in gPosition.a (ColorAttachment3)
            float ssao = texture(ssaoTexture, hitUV).a;
            
            // Distance attenuation
            float dist = length(rayPos - viewOrigin);
            float attenuation = 1.0 - smoothstep(0.0, maxDistance, dist);
            
            // Return indirect contribution with SSAO modulation
            return vec4(hitColor * ssao * attenuation, 1.0);
        }
        
        // March forward
        rayPos += viewDir * stepSize;
    }
    
    // No hit
    return vec4(0.0);
}

// ============================================================================
// Temporal Reprojection
// ============================================================================

// NOTE: This uses depth-only reprojection which will ghost on moving entities.
// For proper handling of dynamic objects, we would need:
// 1. A velocity buffer written during the geometry pass containing per-pixel motion vectors
// 2. Entity shaders modified to output object-space velocity transformed to screen-space
// 3. Velocity-weighted history rejection (discard history if velocity differs significantly)
// Current implementation works well for static geometry and slowly moving camera.

vec3 temporalReproject(vec3 currentGI, vec2 currentUV, float currentDepth) {
    if (temporalEnabled == 0) {
        return currentGI;
    }
    
    // Reconstruct world position
    vec3 viewPos = reconstructViewPos(currentUV, currentDepth);
    vec4 worldPos = invModelViewMatrix * vec4(viewPos, 1.0);
    
    // Reproject to previous frame using previous view-projection matrix
    vec4 prevClipPos = prevViewProjMatrix * worldPos;
    vec2 prevUV = (prevClipPos.xy / prevClipPos.w) * 0.5 + 0.5;
    
    // Check if reprojected position is valid (on screen)
    if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) {
        return currentGI; // Use current frame only
    }
    
    // Sample previous frame's SSGI
    vec3 historyGI = texture(previousSSGI, prevUV).rgb;
    
    // Disocclusion detection using depth comparison
    float prevDepth = texture(previousDepth, prevUV).r;
    float prevLinearDepth = linearizeDepth(prevDepth);
    float currentLinearDepth = linearizeDepth(currentDepth);
    
    // Reject history if depth differs too much (disocclusion)
    float depthThreshold = currentLinearDepth * 0.05; // 5% tolerance
    float depthDiff = abs(prevLinearDepth - currentLinearDepth);
    
    if (depthDiff > depthThreshold) {
        return currentGI; // Disocclusion - use current frame only
    }
    
    // Blend current and history
    // TODO: For moving entities, we should also check velocity difference here
    // and reduce blend factor for pixels with significant motion
    return mix(currentGI, historyGI, temporalBlendFactor);
}

// ============================================================================
// Main
// ============================================================================

void main() {
    // Sample at SSGI buffer resolution
    vec2 fullResUV = uv;
    
    // Sample depth and normal
    float depth = texture(primaryDepth, fullResUV).r;
    vec4 normalSample = texture(gBufferNormal, fullResUV);
    
    // Early out for sky/background (no normal data)
    if (normalSample.a <= 0.0 || depth >= 1.0) {
        outColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    
    // Decode normal from G-buffer (stored as 0-1, convert to -1 to 1)
    vec3 worldNormal = normalize(normalSample.rgb * 2.0 - 1.0);
    
    // Reconstruct view-space position
    vec3 viewPos = reconstructViewPos(fullResUV, depth);
    
    // Transform normal to view space for hemisphere sampling
    mat3 normalMatrix = mat3(modelViewMatrix);
    vec3 viewNormal = normalize(normalMatrix * worldNormal);
    
    // Accumulate indirect lighting from multiple samples
    vec3 indirectLight = vec3(0.0);
    float totalWeight = 0.0;
    
    // Per-pixel jitter seed based on screen position and frame
    // Using Squirrel3 for high-quality random distribution
    vec2 pixelCoord = fullResUV * ssgiBufferSize;
    float frameSeed = float(frameIndex);
    
    // Sample hemisphere using golden angle spiral pattern
    for (int i = 0; i < sampleCount; i++) {
        // Generate pseudo-random values using Squirrel3 hash
        // Each value uses a different Z offset for decorrelation
        float sampleSeed = float(i) + frameSeed * PHI;
        float u1 = Squirrel3HashF(vec3(pixelCoord, sampleSeed));
        float u2 = Squirrel3HashF(vec3(pixelCoord, sampleSeed + 100.0));
        float jitter = Squirrel3HashF(vec3(pixelCoord, sampleSeed + 200.0));
        
        // Sample direction on cosine-weighted hemisphere
        vec3 sampleDir = sampleHemisphere(viewNormal, u1, u2);
        
        // Ray march this direction
        vec4 hitResult = rayMarch(viewPos, sampleDir, jitter);
        
        if (hitResult.a > 0.0) {
            // Weight by cosine of angle to normal (already built into hemisphere sampling)
            float NdotL = max(dot(viewNormal, sampleDir), 0.0);
            indirectLight += hitResult.rgb * NdotL;
            totalWeight += NdotL;
        }
    }
    
    // Normalize accumulated light
    if (totalWeight > 0.0) {
        indirectLight /= totalWeight;
    }
    
    // Apply intensity
    indirectLight *= intensity;
    
    // Apply temporal reprojection for noise reduction
    vec3 finalGI = temporalReproject(indirectLight, fullResUV, depth);
    
    // Output SSGI result
    outColor = vec4(finalGI, 1.0);
}
