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
// capturedScene: Lit geometry captured at end of Opaque stage (before OIT/post-processing)
// This provides raw lit colors without SSAO, bloom, or color grading
uniform sampler2D capturedScene;   // Source of indirect radiance (what surfaces reflect)
uniform sampler2D primaryDepth;    // Depth buffer for position reconstruction
uniform sampler2D gBufferNormal;   // World-space normals for hemisphere orientation
uniform sampler2D ssaoTexture;     // SSAO term in gPosition.a - multiplied into indirect
uniform sampler2D gBufferMaterial; // PBR material: (Reflectivity, Roughness, Metallic, Emissive)

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

// Multi-bounce
uniform int bounceCount;           // Number of light bounces (1-3, default 1)
uniform float bounceAttenuation;   // Energy loss per bounce (0.3-0.6)

// Sky/Sun lighting for rays that escape to sky
uniform vec3 sunPosition;          // Sun direction in view space (normalized)
uniform vec3 sunColor;             // Sun color and intensity
uniform vec3 ambientColor;         // Ambient/sky color

// Constants
const float PI = 3.141592653589793;
const float TAU = 6.283185307179586;
const float GOLDEN_ANGLE = 2.399963229728653;
const float PHI = 1.618033988749895;

// Import Squirrel3 hash for high-quality pseudo-random jittering
@import "./includes/squirrel3.glsl"

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
// Uses world-space distance checks to prevent distant geometry from incorrectly contributing
// Returns: rgb = hit color (including secondary bounces), a = hit success (1.0 or 0.0)
vec4 rayMarch(vec3 viewOrigin, vec3 viewDir, float jitter, float originLinearDepth) {
    const int MAX_STEPS = 16;
    
    // Adaptive step sizing: start small for near-field GI, grow for distant
    // This allows us to catch nearby objects (crates on counters) while still
    // reaching distant surfaces
    const float MIN_STEP = 0.1;     // Minimum step size (blocks) - catches nearby objects
    const float STEP_GROWTH = 1.15; // Exponential growth factor per step
    
    // Calculate total distance covered with exponential stepping
    // sum = MIN_STEP * (STEP_GROWTH^n - 1) / (STEP_GROWTH - 1)
    // We scale to fit maxDistance
    float totalGeometric = MIN_STEP * (pow(STEP_GROWTH, float(MAX_STEPS)) - 1.0) / (STEP_GROWTH - 1.0);
    float stepScale = maxDistance / totalGeometric;
    
    // Start with small offset to avoid self-intersection (much smaller than before)
    float initialOffset = MIN_STEP * stepScale * (0.5 + jitter * 0.5);
    vec3 rayPos = viewOrigin + viewDir * initialOffset;
    float currentStep = MIN_STEP * stepScale;
    
    // Track if ray actually reached sky (not just ran out of steps)
    bool reachedSky = false;
    
    for (int i = 0; i < MAX_STEPS; i++) {
        // Project to screen space
        vec2 hitUV = projectToScreen(rayPos);
        
        // Check bounds with edge fade margin
        // Fade out near edges to reduce popping when geometry leaves screen
        float edgeMargin = 0.02;
        if (hitUV.x < edgeMargin || hitUV.x > (1.0 - edgeMargin) || 
            hitUV.y < edgeMargin || hitUV.y > (1.0 - edgeMargin)) {
            // Ray exited screen - check if pointing upward for sky
            vec3 worldRayDir = normalize((invModelViewMatrix * vec4(viewDir, 0.0)).xyz);
            reachedSky = worldRayDir.y > 0.0;
            break;
        }
        
        // Sample depth at hit point
        float sampledDepth = texture(primaryDepth, hitUV).r;
        
        // Check for sky pixels (depth >= 0.9999)
        if (sampledDepth >= 0.9999) {
            // Ray hit sky in the depth buffer
            reachedSky = true;
            break;
        }
        
        float sampledLinearDepth = linearizeDepth(sampledDepth);
        
        // Reject samples that are extremely far (near horizon)
        // These cause artifacts due to depth precision loss at distance
        float maxReliableDepth = zFar * 0.8; // 80% of far plane
        if (sampledLinearDepth > maxReliableDepth) {
            rayPos += viewDir * currentStep;
            currentStep *= STEP_GROWTH;
            continue;
        }
        
        float rayLinearDepth = -rayPos.z; // View space Z is negative forward
        
        // Check for intersection (ray is behind surface)
        float depthDiff = rayLinearDepth - sampledLinearDepth;
        
        // World-space distance check: reject hits from surfaces that are much farther
        // than expected based on the ray's travel distance
        // This prevents distant geometry from incorrectly contributing light
        float expectedDepth = originLinearDepth + length(rayPos - viewOrigin);
        float depthRatio = sampledLinearDepth / max(expectedDepth, 0.1);
        
        // Reject if the sampled surface is MUCH farther than we should have traveled
        // (indicates we're looking "through" nearby geometry at distant objects)
        // Relaxed threshold: allow up to 3x expected depth
        if (depthRatio > 3.0) {
            rayPos += viewDir * currentStep;
            currentStep *= STEP_GROWTH;
            continue;
        }
        
        // Adaptive thickness: smaller for near samples, larger for far
        float adaptiveThickness = rayThickness * (0.5 + 0.5 * (currentStep / (MIN_STEP * stepScale)));
        
        if (depthDiff > 0.0 && depthDiff < adaptiveThickness) {
            // Hit! Verify this is a valid surface by checking its normal
            vec4 hitNormalSample = texture(gBufferNormal, hitUV);
            
            // Skip if no valid normal data (background/sky)
            if (hitNormalSample.a <= 0.0) {
                rayPos += viewDir * currentStep;
                currentStep *= STEP_GROWTH;
                continue;
            }
            
            // Sample material properties - emissive surfaces bypass back-face rejection
            // Material format: (Roughness, Metallic, Emissive, Reflectivity)
            vec4 material = texture(gBufferMaterial, hitUV);
            float emissive = material.b; // Emissive is in blue channel
            
            // Decode hit surface normal
            vec3 hitWorldNormal = normalize(hitNormalSample.rgb * 2.0 - 1.0);
            mat3 normalMatrix = mat3(modelViewMatrix);
            vec3 hitViewNormal = normalize(normalMatrix * hitWorldNormal);
            
            // Emitter cosine: Lambert's law - surfaces emit proportional to cos(angle)
            // This is NdotL at the emitting surface back toward the receiver
            vec3 toOrigin = normalize(viewOrigin - rayPos);
            float emitterCosine = dot(hitViewNormal, toOrigin);
            
            // For non-emissive surfaces: use emitter cosine (negative = back-facing = reject)
            // For emissive surfaces: emit in all directions (cosine = 1.0)
            float emissiveFactor = smoothstep(0.0, 0.1, emissive);
            
            // Back-face rejection for non-emissive
            if (emitterCosine <= 0.0 && emissiveFactor < 0.5) {
                rayPos += viewDir * currentStep;
                currentStep *= STEP_GROWTH;
                continue;
            }
            
            // Apply emitter cosine for diffuse surfaces, bypass for emissive
            // Emissive surfaces emit uniformly, non-emissive follow Lambert's law
            float emitterWeight = mix(max(0.0, emitterCosine), 1.0, emissiveFactor);
            
            // Sample the captured scene color at this point (first bounce - direct lighting)
            vec3 hitColor = texture(capturedScene, hitUV).rgb;
            
            // Multi-bounce: sample previous frame's SSGI at hit point for secondary bounces
            // Only for non-emissive surfaces - emissive surfaces are primary light sources
            // This gives us light that bounced off other surfaces in previous frames
            if (bounceCount > 1) {
                vec3 secondaryBounce = texture(previousSSGI, hitUV).rgb;
                // Add secondary bounce contribution with attenuation
                // Each bounce loses energy (typical albedo ~0.5)
                // Reduce secondary bounce contribution for emissive surfaces (they're sources, not receivers)
                float bounceWeight = 1.0 - emissiveFactor;
                hitColor += secondaryBounce * bounceAttenuation * bounceWeight;
                
                // Third bounce (from previous frame's accumulated bounces)
                if (bounceCount > 2) {
                    // The previousSSGI already contains some secondary bounces,
                    // so we add a fraction of that as tertiary contribution
                    hitColor += secondaryBounce * bounceAttenuation * bounceAttenuation * 0.5 * bounceWeight;
                }
            }
            
            // SSAO modulation: emissive surfaces emit light and shouldn't be darkened by AO
            // Blend between full SSAO (non-emissive) and no SSAO (emissive)
            float ssao = texture(ssaoTexture, hitUV).a;
            float ssaoWeight = mix(ssao, 1.0, emissiveFactor);
            
            // Distance attenuation based on actual 3D distance traveled
            float dist = length(rayPos - viewOrigin);
            float attenuation = 1.0 - smoothstep(0.0, maxDistance, dist);
            
            // Depth-based attenuation: fade out samples from very distant surfaces
            // This prevents horizon terrain from contributing indirect light
            // Start fading at 32 blocks, fully faded by 64 blocks
            float depthFade = 1.0 - smoothstep(32.0, 64.0, sampledLinearDepth);
            attenuation *= depthFade;
            
            // Edge fade: reduce contribution near screen edges
            float edgeFade = smoothstep(0.0, 0.1, min(hitUV.x, min(1.0 - hitUV.x, min(hitUV.y, 1.0 - hitUV.y))));
            attenuation *= edgeFade;
            
            // Apply emitter cosine (Lambert's law for diffuse emission)
            // This accounts for surfaces emitting less light at grazing angles
            attenuation *= emitterWeight;
            
            // Emissive boost: light sources contribute more than regular surfaces
            // Scale by emissive intensity for brighter lights to cast more indirect
            float emissiveBoost = 1.0 + emissive * 2.0;
            
            // Return indirect contribution
            return vec4(hitColor * ssaoWeight * attenuation * emissiveBoost, 1.0);
        }
        
        // March forward with adaptive step (grows each iteration)
        rayPos += viewDir * currentStep;
        currentStep *= STEP_GROWTH;
    }
    
    // Only sample sky if ray actually reached sky (hit sky depth or exited screen upward)
    // Rays that just ran out of steps without reaching sky return black
    if (!reachedSky) {
        return vec4(0.0);
    }
    
    // Sample sky/sun lighting for rays that escaped to sky
    vec3 worldRayDir = normalize((invModelViewMatrix * vec4(viewDir, 0.0)).xyz);
    
    // Sky contribution: fade in based on how much ray points upward
    float skyWeight = smoothstep(0.0, 0.3, max(0.0, worldRayDir.y)); // Gradual fade from horizon
    vec3 skyContribution = ambientColor * skyWeight;
    
    // Sun contribution: directional light when ray points toward sun
    float sunDot = max(0.0, dot(worldRayDir, sunPosition));
    // Soft sun disk with atmospheric scattering falloff
    float sunFactor = pow(sunDot, 16.0); // Moderate falloff
    vec3 sunContribution = sunColor * sunFactor;
    
    // Combine sky and sun - HDR values are fine, will be tonemapped later
    // Attenuate since this is indirect (bounced) light, not direct
    vec3 skyLight = (skyContribution + sunContribution) * 0.2;
    
    // NaN/inf protection - if any component is invalid, return zero
    if (any(isnan(skyLight)) || any(isinf(skyLight))) {
        return vec4(0.0);
    }
    
    return vec4(skyLight, 1.0);
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
    
    // Disocclusion detection using depth comparison BEFORE sampling history
    // This prevents blending with invalid/stale history data
    float prevDepth = texture(previousDepth, prevUV).r;
    float prevLinearDepth = linearizeDepth(prevDepth);
    float currentLinearDepth = linearizeDepth(currentDepth);
    
    // Reject history if depth differs too much (disocclusion)
    // Relaxed threshold: 10% tolerance to allow more temporal blending
    float depthThreshold = currentLinearDepth * 0.1;
    float depthDiff = abs(prevLinearDepth - currentLinearDepth);
    
    if (depthDiff > depthThreshold) {
        return currentGI; // Disocclusion - use current frame only
    }
    
    // Only sample history after validation passes
    vec3 historyGI = texture(previousSSGI, prevUV).rgb;
    
    // Neighborhood clamping to reduce sparkle from outlier samples
    // Sample neighbors to find a reasonable range for the current pixel
    vec2 texelSize = 1.0 / ssgiBufferSize;
    vec3 minNeighbor = currentGI;
    vec3 maxNeighbor = currentGI;
    
    // Sample 4 neighbors (cross pattern for efficiency)
    for (int i = 0; i < 4; i++) {
        vec2 offset = vec2(
            (i == 0) ? -texelSize.x : ((i == 1) ? texelSize.x : 0.0),
            (i == 2) ? -texelSize.y : ((i == 3) ? texelSize.y : 0.0)
        );
        vec2 neighborUV = currentUV + offset;
        
        // Sample neighbor's current frame result (approximate via depth-based estimation)
        // For simplicity, we just expand the min/max bounds slightly
        float neighborDepth = texture(primaryDepth, neighborUV).r;
        if (neighborDepth < 1.0) {
            // Expand bounds based on current sample variance
            vec3 variance = abs(currentGI - historyGI) * 0.5;
            minNeighbor = min(minNeighbor, currentGI - variance);
            maxNeighbor = max(maxNeighbor, currentGI + variance);
        }
    }
    
    // Clamp history to neighborhood bounds to reduce sparkle
    vec3 clampedHistory = clamp(historyGI, minNeighbor, maxNeighbor);
    
    // Blend current and history with clamped result
    // Use higher blend factor for smoother accumulation
    return mix(currentGI, clampedHistory, temporalBlendFactor);
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
    float originLinearDepth = linearizeDepth(depth);
    
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
        // The cosine-weighted distribution already accounts for the receiver's NdotL term
        // (samples are distributed proportionally to cos(θ), so PDF = cos(θ)/PI)
        vec3 sampleDir = sampleHemisphere(viewNormal, u1, u2);
        
        // Ray march this direction with world-space distance validation
        vec4 hitResult = rayMarch(viewPos, sampleDir, jitter, originLinearDepth);
        
        // With cosine-weighted sampling, we simply average the results
        // The receiver cosine is implicit in the sampling distribution
        // Only the emitter cosine needs to be applied (done in rayMarch)
        if (hitResult.a > 0.0) {
            indirectLight += hitResult.rgb;
            totalWeight += 1.0;
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
